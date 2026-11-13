using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// How urgent a message to a browser guest is, which decides what is dropped when its connection
// falls behind.
public enum StreamMessageKind
{
    Control,
    Sound,
    PictureKeyframe,
    Picture
}

// The way to reach a guest that is connected by something other than the host's UDP socket.
public interface IStreamGuestLink
{
    // message is handed over for good and may be sent from another thread.
    void Send(byte[] message, StreamMessageKind kind);
}

// The host's side of the connection: accepts guests, splits each encoded frame into UDP
// packets and sends them to every guest.
public sealed class StreamHostSession : IDisposable
{
    private const int GuestTimeoutMs = 5000;
    private const int FormatRepeatMs = 1000;

    private sealed class Guest
    {
        public IPEndPoint EndPoint;
        public uint GuestId;
        public long LastHeardMs;
        public uint LastInputSequence;
        public bool HasInputSequence;
        // Set for a guest in a web browser; null for a RomBox guest, which is sent UDP packets.
        public IStreamGuestLink Link;
    }

    private readonly Socket socket;
    private readonly Action<string> log;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly Dictionary<IPEndPoint, Guest> guests = new Dictionary<IPEndPoint, Guest>();
    private readonly object guestLock = new object();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<IPEndPoint, IStreamGuestLink> links = new System.Collections.Concurrent.ConcurrentDictionary<IPEndPoint, IStreamGuestLink>();
    private readonly byte[] chunkBuffer = new byte[StreamProtocol.VideoChunkHeaderSize + StreamProtocol.MaxChunkPayload];
    private readonly Thread receiveThread;
    private readonly Timer housekeepingTimer;

    private volatile bool disposed;
    private byte[] formatPacket;
    private uint nextFormatId = 1;
    private uint nextFrameId = 1;
    private uint nextAudioSequence = 1;
    private readonly byte[] audioBuffer = new byte[StreamProtocol.AudioHeaderSize + StreamProtocol.MaxChunkPayload];

    public int Port { get; }
    public int GuestCount { get { lock (guestLock) return guests.Count; } }
    public long BytesSent { get; private set; }
    public long FramesSent { get; private set; }
    public long KeyframesSent { get; private set; }
    public long AudioFramesSent { get; private set; }

    public event Action<IPEndPoint> GuestJoined;
    public event Action<IPEndPoint> GuestLeft;
    public event Action KeyframeRequested;
    // Guest, pad index, whether the pad is plugged in, its state. Raised on the receive thread.
    public event Action<IPEndPoint, int, bool, StreamPadState> GuestInputReceived;

    public StreamHostSession(int port, Action<string> log)
    {
        this.log = log;
        socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        socket.DualMode = true;
        socket.SendBufferSize = 4 * 1024 * 1024;
        IgnoreConnectionResets(socket);
        socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
        Port = ((IPEndPoint)socket.LocalEndPoint).Port;

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Stream host receive" };
        receiveThread.Start();
        housekeepingTimer = new Timer(_ => Housekeeping(), null, FormatRepeatMs, FormatRepeatMs);
        log?.Invoke($"[Stream] hosting on UDP port {Port}.");
    }

    public uint ClockMs => (uint)clock.ElapsedMilliseconds;

    // A new format id tells guests to restart their decoder.
    public void SetVideoFormat(StreamVideoFormat format)
    {
        format.FormatId = nextFormatId++;
        byte[] packet = StreamProtocol.BuildVideoFormat(format);
        formatPacket = packet;
        log?.Invoke($"[Stream] video is {format.Width}x{format.Height} at {format.FrameRate} fps.");

        foreach (Guest guest in SnapshotGuests())
        {
            Send(packet, packet.Length, guest.EndPoint);
        }
    }

    public void SendFrame(byte[] frame, bool isKeyframe)
    {
        Guest[] targets = SnapshotGuests();
        uint frameId = nextFrameId++;
        // Counted with or without guests, so the log shows what the capture delivers.
        FramesSent++;

        if (isKeyframe)
        {
            KeyframesSent++;
        }

        if (targets.Length == 0)
        {
            return;
        }

        uint hostTime = ClockMs;
        SendWholeFrame(targets, frame, frameId, isKeyframe, hostTime);

        if (targets.All(guest => guest.Link != null))
        {
            return;
        }

        int chunkCount = (frame.Length + StreamProtocol.MaxChunkPayload - 1) / StreamProtocol.MaxChunkPayload;

        if (chunkCount > ushort.MaxValue)
        {
            log?.Invoke($"[Stream] skipped a {frame.Length}-byte frame; it is too large to send.");
            return;
        }

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int offset = chunkIndex * StreamProtocol.MaxChunkPayload;
            int length = Math.Min(StreamProtocol.MaxChunkPayload, frame.Length - offset);
            int headerSize = StreamProtocol.WriteVideoChunkHeader(chunkBuffer, frameId, (ushort)chunkIndex, (ushort)chunkCount, isKeyframe, hostTime);
            Buffer.BlockCopy(frame, offset, chunkBuffer, headerSize, length);

            foreach (Guest guest in targets)
            {
                if (guest.Link == null)
                {
                    Send(chunkBuffer, headerSize + length, guest.EndPoint);
                }
            }
        }
    }

    // A browser guest's connection carries messages of any size, so it gets the frame as a
    // single chunk. One copy is shared by all of them.
    private void SendWholeFrame(Guest[] targets, byte[] frame, uint frameId, bool isKeyframe, uint hostTime)
    {
        byte[] message = null;

        foreach (Guest guest in targets)
        {
            if (guest.Link == null)
            {
                continue;
            }

            if (message == null)
            {
                message = new byte[StreamProtocol.VideoChunkHeaderSize + frame.Length];
                int headerSize = StreamProtocol.WriteVideoChunkHeader(message, frameId, 0, 1, isKeyframe, hostTime);
                Buffer.BlockCopy(frame, 0, message, headerSize, frame.Length);
            }

            guest.Link.Send(message, isKeyframe ? StreamMessageKind.PictureKeyframe : StreamMessageKind.Picture);
            BytesSent += message.Length;
        }
    }

    // A guest in a web browser. Its messages are the same packets a RomBox guest sends by UDP,
    // handed over with Receive, and everything for it goes out through the link. endPoint only
    // has to tell this guest apart from the others.
    public void AddLink(IPEndPoint endPoint, IStreamGuestLink link)
    {
        links[endPoint] = link;
    }

    public void RemoveLink(IPEndPoint endPoint)
    {
        if (links.TryRemove(endPoint, out _))
        {
            RemoveGuest(endPoint, "left");
        }
    }

    public void Receive(IPEndPoint endPoint, byte[] message)
    {
        if (!disposed && links.ContainsKey(endPoint) && StreamProtocol.TryReadHeader(message, out StreamPacketType type))
        {
            HandlePacket(type, message, message.Length, endPoint);
        }
    }

    // A browser guest fell behind and had frames dropped; it can carry on from a keyframe.
    public void RequestKeyframe()
    {
        KeyframeRequested?.Invoke();
    }

    // One Opus frame. Called from the audio capture thread only.
    public void SendAudio(byte[] opusFrame)
    {
        uint sequence = nextAudioSequence++;
        Guest[] targets = SnapshotGuests();

        if (targets.Length == 0 || opusFrame.Length > StreamProtocol.MaxChunkPayload)
        {
            return;
        }

        AudioFramesSent++;
        StreamProtocol.WriteHeader(audioBuffer, StreamPacketType.Audio);
        BinaryPrimitives.WriteUInt32LittleEndian(audioBuffer.AsSpan(4), sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(audioBuffer.AsSpan(8), ClockMs);
        Buffer.BlockCopy(opusFrame, 0, audioBuffer, StreamProtocol.AudioHeaderSize, opusFrame.Length);

        foreach (Guest guest in targets)
        {
            Send(audioBuffer, StreamProtocol.AudioHeaderSize + opusFrame.Length, guest.EndPoint, StreamMessageKind.Sound);
        }
    }

    // The id a guest gave when it said hello. A lobby uses it to tell which player a guest is.
    public bool TryGetGuestId(IPEndPoint endPoint, out uint guestId)
    {
        lock (guestLock)
        {
            bool found = guests.TryGetValue(endPoint, out Guest guest);
            guestId = found ? guest.GuestId : 0;
            return found;
        }
    }

    private Guest[] SnapshotGuests()
    {
        lock (guestLock)
        {
            return guests.Values.ToArray();
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[2048];
        EndPoint remote = new IPEndPoint(IPAddress.IPv6Any, 0);

        while (!disposed)
        {
            int length;

            try
            {
                length = socket.ReceiveFrom(buffer, ref remote);
            }
            catch (SocketException)
            {
                continue;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            if (!StreamProtocol.TryReadHeader(buffer.AsSpan(0, length), out StreamPacketType type))
            {
                continue;
            }

            var sender = (IPEndPoint)remote;
            HandlePacket(type, buffer, length, new IPEndPoint(sender.Address, sender.Port));
        }
    }

    private void HandlePacket(StreamPacketType type, byte[] buffer, int length, IPEndPoint sender)
    {
        switch (type)
        {
            case StreamPacketType.Hello when length >= StreamProtocol.HelloSize:
                HandleHello(sender, BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)));
                break;
            case StreamPacketType.KeyframeRequest:
                if (TouchGuest(sender))
                {
                    KeyframeRequested?.Invoke();
                }
                break;
            case StreamPacketType.Ping when length >= StreamProtocol.PingSize:
                if (TouchGuest(sender))
                {
                    byte[] pong = new byte[StreamProtocol.PongSize];
                    StreamProtocol.WriteHeader(pong, StreamPacketType.Pong);
                    Buffer.BlockCopy(buffer, 4, pong, 4, 8);
                    BinaryPrimitives.WriteUInt32LittleEndian(pong.AsSpan(12), ClockMs);
                    Send(pong, pong.Length, sender);
                }
                break;
            case StreamPacketType.GuestInput:
                if (StreamProtocol.TryReadGuestInput(buffer.AsSpan(0, length), out uint sequence, out int padIndex, out bool isConnected, out StreamPadState state)
                    && AcceptInputSequence(sender, sequence))
                {
                    GuestInputReceived?.Invoke(sender, padIndex, isConnected, state);
                }
                break;
            case StreamPacketType.Bye:
                RemoveGuest(sender, "left");
                break;
        }
    }

    private void HandleHello(IPEndPoint sender, uint guestId)
    {
        bool isNew;

        lock (guestLock)
        {
            isNew = !guests.TryGetValue(sender, out Guest guest) || guest.GuestId != guestId;

            if (isNew)
            {
                links.TryGetValue(sender, out IStreamGuestLink link);
                guests[sender] = new Guest { EndPoint = sender, GuestId = guestId, Link = link };
            }

            guests[sender].LastHeardMs = clock.ElapsedMilliseconds;
        }

        byte[] welcome = new byte[StreamProtocol.HelloSize];
        StreamProtocol.WriteHeader(welcome, StreamPacketType.Welcome);
        BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(4), guestId);
        Send(welcome, welcome.Length, sender);

        if (!isNew)
        {
            return;
        }

        log?.Invoke($"[Stream] guest joined from {sender}.");

        if (formatPacket != null)
        {
            Send(formatPacket, formatPacket.Length, sender);
        }

        GuestJoined?.Invoke(sender);
        // A guest that joins between keyframes waits for the next one; asking now shortens that.
        KeyframeRequested?.Invoke();
    }

    private bool TouchGuest(IPEndPoint sender)
    {
        lock (guestLock)
        {
            if (!guests.TryGetValue(sender, out Guest guest))
            {
                return false;
            }

            guest.LastHeardMs = clock.ElapsedMilliseconds;
            return true;
        }
    }

    // UDP can deliver packets out of order. An older state must not overwrite a newer one.
    private bool AcceptInputSequence(IPEndPoint sender, uint sequence)
    {
        lock (guestLock)
        {
            if (!guests.TryGetValue(sender, out Guest guest))
            {
                return false;
            }

            guest.LastHeardMs = clock.ElapsedMilliseconds;

            if (guest.HasInputSequence && (int)(sequence - guest.LastInputSequence) <= 0)
            {
                return false;
            }

            guest.LastInputSequence = sequence;
            guest.HasInputSequence = true;
            return true;
        }
    }

    private void RemoveGuest(IPEndPoint endPoint, string reason)
    {
        bool removed;

        lock (guestLock)
        {
            removed = guests.Remove(endPoint);
        }

        if (removed)
        {
            log?.Invoke($"[Stream] guest {endPoint} {reason}.");
            GuestLeft?.Invoke(endPoint);
        }
    }

    private void Housekeeping()
    {
        if (disposed)
        {
            return;
        }

        long now = clock.ElapsedMilliseconds;

        foreach (Guest guest in SnapshotGuests())
        {
            if (now - guest.LastHeardMs > GuestTimeoutMs)
            {
                RemoveGuest(guest.EndPoint, "stopped answering");
            }
            else if (formatPacket != null)
            {
                Send(formatPacket, formatPacket.Length, guest.EndPoint);
            }
        }
    }

    private void Send(byte[] packet, int length, IPEndPoint endPoint, StreamMessageKind kind = StreamMessageKind.Control)
    {
        if (!links.IsEmpty && links.TryGetValue(endPoint, out IStreamGuestLink link))
        {
            // The link sends from another thread, so it gets a copy of its own.
            link.Send(packet.AsSpan(0, length).ToArray(), kind);
            BytesSent += length;
            return;
        }

        try
        {
            socket.SendTo(packet, 0, length, SocketFlags.None, endPoint);
            BytesSent += length;
        }
        catch (SocketException exception)
        {
            log?.Invoke($"[Stream] sending to {endPoint} failed: {exception.SocketErrorCode}.");
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Windows reports an ICMP "port unreachable" from an earlier send as an error on the next
    // receive. Turned off, a guest that closes without saying goodbye cannot disturb the others.
    internal static void IgnoreConnectionResets(Socket udpSocket)
    {
        if (OperatingSystem.IsWindows())
        {
            const int SioUdpConnReset = -1744830452;
            udpSocket.IOControl(SioUdpConnReset, new byte[] { 0 }, null);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        housekeepingTimer.Dispose();

        byte[] bye = new byte[StreamProtocol.HeaderSize];
        StreamProtocol.WriteHeader(bye, StreamPacketType.Bye);

        foreach (Guest guest in SnapshotGuests())
        {
            Send(bye, bye.Length, guest.EndPoint);
        }

        socket.Dispose();
    }
}
