using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Net;

// The guests who joined from a web page. They can connect before a game is running: a lobby
// opens this when hosting starts, lists who has joined, and hands it to the stream once the game
// starts. From then on each browser is a guest like any other, sending the same packets a RomBox
// guest sends by UDP and receiving the same picture and sound.
public sealed class StreamBrowserGuests : IDisposable
{
    public sealed class Guest
    {
        // 0 until the browser has said hello.
        public uint GuestId;
        public IPEndPoint EndPoint;
        public int ControllerCount;
        internal WebTransportSession Session;
        internal readonly bool[] PadConnected = new bool[StreamProtocol.MaxPadsPerGuest];
    }

    private readonly WebTransportListener listener;
    private readonly Action<string> log;
    private readonly object gate = new object();
    private readonly List<Guest> guests = new List<Guest>();
    private StreamHostSession host;
    private bool disposed;

    // Raised on a worker thread when a browser joins, leaves or changes its controllers.
    public event Action GuestsChanged;

    public static bool IsSupported
    {
        get
        {
            try
            {
                return WebTransportListener.IsSupported;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    public int Port => listener.Port;

    // What the join page needs besides the host's address: the port and the fingerprint of this
    // session's certificate.
    public string JoinCode => $"{listener.Port}/{listener.CertificateFingerprint}";

    public StreamBrowserGuests(int port, Action<string> log)
    {
        this.log = log;
        listener = new WebTransportListener(port, log);
        listener.SessionOpened += OnSessionOpened;
    }

    // The browsers that have said hello.
    public Guest[] Snapshot()
    {
        lock (gate)
        {
            return guests.Where(guest => guest.GuestId != 0).Select(guest => new Guest { GuestId = guest.GuestId, EndPoint = guest.EndPoint, ControllerCount = guest.ControllerCount }).ToArray();
        }
    }

    // Makes every browser, here now or joining later, a guest of this stream.
    public void Attach(StreamHostSession session)
    {
        lock (gate)
        {
            host = session;

            foreach (Guest guest in guests)
            {
                session.AddLink(guest.EndPoint, guest.Session);
            }
        }
    }

    // The stream has ended. The browsers stay connected and wait for the next one.
    public void Detach()
    {
        lock (gate)
        {
            if (host == null)
            {
                return;
            }

            foreach (Guest guest in guests)
            {
                host.RemoveLink(guest.EndPoint);
            }

            host = null;
        }
    }

    private void OnSessionOpened(WebTransportSession session)
    {
        var guest = new Guest { EndPoint = session.RemoteEndPoint, Session = session };

        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            guests.Add(guest);
            host?.AddLink(guest.EndPoint, session);
        }

        session.MessageReceived += message => OnMessage(guest, message);
        session.KeyframeNeeded += () => CurrentHost()?.RequestKeyframe();
        session.Closed += () => OnClosed(guest);

        if (session.IsClosed)
        {
            OnClosed(guest);
        }
    }

    private StreamHostSession CurrentHost()
    {
        lock (gate)
        {
            return host;
        }
    }

    private void OnMessage(Guest guest, byte[] message)
    {
        if (!StreamProtocol.TryReadHeader(message, out StreamPacketType type))
        {
            return;
        }

        bool changed = false;
        StreamHostSession current;

        lock (gate)
        {
            current = host;

            if (type == StreamPacketType.Hello && message.Length >= StreamProtocol.HelloSize)
            {
                uint guestId = BinaryPrimitives.ReadUInt32LittleEndian(message.AsSpan(4));
                changed = guest.GuestId != guestId;
                guest.GuestId = guestId;
            }
            else if (type == StreamPacketType.GuestInput && StreamProtocol.TryReadGuestInput(message, out _, out int padIndex, out bool isConnected, out _))
            {
                changed = guest.PadConnected[padIndex] != isConnected;
                guest.PadConnected[padIndex] = isConnected;
                guest.ControllerCount = guest.PadConnected.Count(connected => connected);
            }
        }

        if (current != null)
        {
            current.Receive(guest.EndPoint, message);
        }
        else if (type == StreamPacketType.Hello && message.Length >= StreamProtocol.HelloSize)
        {
            // With no game running there is no stream to answer, so the hello is answered here.
            byte[] welcome = new byte[StreamProtocol.HelloSize];
            StreamProtocol.WriteHeader(welcome, StreamPacketType.Welcome);
            Buffer.BlockCopy(message, 4, welcome, 4, 4);
            guest.Session.Send(welcome, StreamMessageKind.Control);
        }

        if (changed)
        {
            if (type == StreamPacketType.Hello)
            {
                log?.Invoke($"[Stream] a browser joined from {guest.EndPoint}.");
            }

            GuestsChanged?.Invoke();
        }
    }

    private void OnClosed(Guest guest)
    {
        bool wasKnown;

        lock (gate)
        {
            wasKnown = guests.Remove(guest);
            host?.RemoveLink(guest.EndPoint);
        }

        if (wasKnown)
        {
            log?.Invoke($"[Stream] the browser at {guest.EndPoint} left.");
            GuestsChanged?.Invoke();
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
        }

        Detach();
        listener.Dispose();
    }
}
