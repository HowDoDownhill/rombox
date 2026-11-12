using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

// One virtual Xbox 360 controller on the host. Emulators see it as a real one.
public interface IStreamVirtualPad : IDisposable
{
    void Submit(in StreamPadState state);
}

// Creates virtual controllers through whatever the host's operating system offers.
public interface IStreamVirtualPadBackend : IDisposable
{
    // Null when controllers can be created; otherwise what the player has to do, in plain words.
    string UnavailableReason { get; }
    IStreamVirtualPad CreatePad();
}

public static class StreamVirtualPadBackends
{
    public static IStreamVirtualPadBackend Create(Action<string> log)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateViGEm(log);
        }

        if (OperatingSystem.IsLinux())
        {
            return new UinputPadBackend(log);
        }

        return new MissingPadBackend("Guest controllers are not supported on this system yet.");
    }

    // Null when this computer can plug in controllers for guests; otherwise what is missing.
    public static string FindMissingRequirement()
    {
        using IStreamVirtualPadBackend backend = Create(null);
        return backend.UnavailableReason;
    }

    // Kept out of Create so the ViGEm assembly is only loaded on Windows.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IStreamVirtualPadBackend CreateViGEm(Action<string> log)
    {
        return new ViGEmPadBackend(log);
    }
}

public sealed class MissingPadBackend : IStreamVirtualPadBackend
{
    public MissingPadBackend(string reason)
    {
        UnavailableReason = reason;
    }

    public string UnavailableReason { get; }
    public IStreamVirtualPad CreatePad() => null;

    public void Dispose()
    {
    }
}

// Gives each guest controller a virtual controller on the host and feeds it the guest's input.
// A lobby reserves controllers before the game starts, so the emulator finds every player's
// controller plugged in when it launches; those stay plugged in until the stream ends. Without a
// reservation a controller appears when its first input arrives and goes when the guest unplugs
// it or leaves.
public sealed class StreamHostPads : IDisposable
{
    // XInput has four slots, and games rarely take more players.
    public const int MaximumPads = 4;

    // A guest repeats its state every 100 ms. After this long without one, its connection has
    // stalled, and whatever it was holding is let go so the game does not keep running left.
    private const long ReleaseAfterSilenceMs = 400;

    private sealed class Entry
    {
        public IStreamVirtualPad Pad;
        public long LastInputMs;
        public bool IsReleased;
    }

    private readonly StreamHostSession session;
    private readonly IStreamVirtualPadBackend backend;
    private readonly Action<string> log;
    private readonly Dictionary<(IPEndPoint Guest, int PadIndex), Entry> pads = new Dictionary<(IPEndPoint, int), Entry>();
    private readonly Dictionary<(uint GuestId, int PadIndex), Entry> reservedPads = new Dictionary<(uint, int), Entry>();
    private readonly object padLock = new object();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Timer silenceTimer;
    private bool reportedFull;
    private bool disposed;

    public int PadCount { get { lock (padLock) return PadCountUnlocked; } }

    private int PadCountUnlocked => pads.Count + reservedPads.Count;

    // False in a lobby: only the controllers reserved before launch exist, because the emulator
    // was set up for exactly those.
    public bool AllowsUnreservedPads { get; set; } = true;

    // Plugs in controllers for a guest that has not connected yet. Returns how many were made.
    public int Reserve(uint guestId, int padCount)
    {
        lock (padLock)
        {
            int created = 0;

            for (int padIndex = 0; padIndex < padCount && !disposed && PadCountUnlocked < MaximumPads; padIndex++)
            {
                if (reservedPads.ContainsKey((guestId, padIndex)))
                {
                    continue;
                }

                IStreamVirtualPad pad = backend.UnavailableReason == null ? backend.CreatePad() : null;

                if (pad == null)
                {
                    break;
                }

                reservedPads[(guestId, padIndex)] = new Entry { Pad = pad, IsReleased = true };
                created++;
            }

            log?.Invoke($"[Stream] reserved {created} of {padCount} controller(s) for guest {guestId}.");
            return created;
        }
    }

    public StreamHostPads(StreamHostSession session, IStreamVirtualPadBackend backend, Action<string> log)
    {
        this.session = session;
        this.backend = backend;
        this.log = log;

        if (backend.UnavailableReason != null)
        {
            log?.Invoke($"[Stream] guests cannot use controllers: {backend.UnavailableReason}");
            return;
        }

        session.GuestInputReceived += OnGuestInput;
        session.GuestLeft += OnGuestLeft;
        silenceTimer = new Timer(_ => ReleaseSilentPads(), null, 100, 100);
    }

    private void ReleaseSilentPads()
    {
        lock (padLock)
        {
            long now = clock.ElapsedMilliseconds;

            foreach (Entry entry in pads.Values.Concat(reservedPads.Values))
            {
                if (!entry.IsReleased && now - entry.LastInputMs > ReleaseAfterSilenceMs)
                {
                    entry.IsReleased = true;
                    entry.Pad.Submit(default);
                }
            }
        }
    }

    // Runs on the host's receive thread, so the input reaches the controller without waiting for a frame.
    private void OnGuestInput(IPEndPoint guest, int padIndex, bool isConnected, StreamPadState state)
    {
        lock (padLock)
        {
            if (disposed)
            {
                return;
            }

            if (session.TryGetGuestId(guest, out uint guestId) && reservedPads.TryGetValue((guestId, padIndex), out Entry reserved))
            {
                // A reserved controller stays plugged in when the guest unplugs theirs, so the
                // other players keep their places.
                reserved.LastInputMs = clock.ElapsedMilliseconds;
                reserved.IsReleased = !isConnected;
                reserved.Pad.Submit(isConnected ? state : default);
                return;
            }

            if (!AllowsUnreservedPads)
            {
                return;
            }

            var key = (guest, padIndex);
            pads.TryGetValue(key, out Entry entry);

            if (!isConnected)
            {
                if (entry != null)
                {
                    pads.Remove(key);
                    entry.Pad.Dispose();
                    log?.Invoke($"[Stream] controller {padIndex + 1} of guest {guest} was unplugged.");
                }

                return;
            }

            if (entry == null)
            {
                IStreamVirtualPad pad = CreatePad(guest, padIndex);

                if (pad == null)
                {
                    return;
                }

                entry = new Entry { Pad = pad };
                pads[key] = entry;
            }

            entry.LastInputMs = clock.ElapsedMilliseconds;
            entry.IsReleased = false;
            entry.Pad.Submit(state);
        }
    }

    private IStreamVirtualPad CreatePad(IPEndPoint guest, int padIndex)
    {
        if (PadCountUnlocked >= MaximumPads)
        {
            if (!reportedFull)
            {
                reportedFull = true;
                log?.Invoke($"[Stream] all {MaximumPads} guest controllers are in use; controller {padIndex + 1} of guest {guest} was not added.");
            }

            return null;
        }

        IStreamVirtualPad pad = backend.CreatePad();

        if (pad != null)
        {
            log?.Invoke($"[Stream] controller {padIndex + 1} of guest {guest} is plugged in ({pads.Count + 1} guest controller(s)).");
        }

        return pad;
    }

    private void OnGuestLeft(IPEndPoint guest)
    {
        lock (padLock)
        {
            foreach (var key in pads.Keys.Where(key => key.Guest.Equals(guest)).ToList())
            {
                pads[key].Pad.Dispose();
                pads.Remove(key);
            }

            reportedFull = false;
        }
    }

    public void Dispose()
    {
        session.GuestInputReceived -= OnGuestInput;
        session.GuestLeft -= OnGuestLeft;
        silenceTimer?.Dispose();

        lock (padLock)
        {
            disposed = true;

            foreach (Entry entry in pads.Values.Concat(reservedPads.Values))
            {
                entry.Pad.Dispose();
            }

            reservedPads.Clear();

            pads.Clear();
        }

        backend.Dispose();
    }
}
