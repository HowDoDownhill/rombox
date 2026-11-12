using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// The guest's side of the connection: says hello until the host answers, keeps the connection
// alive, rebuilds frames from their chunks and measures the round trip.
public sealed class StreamGuestSession : IDisposable
{
    private const int HelloWhileWaitingMs = 500;
    private const int KeepAliveMs = 1000;
    private const int KeyframeRequestGapMs = 250;
    private const int SilenceTimeoutMs = 4000;

    private readonly Socket socket;
    private readonly IPEndPoint host;
    private readonly Action<string> log;
    private readonly uint guestId;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly FrameReassembler reassembler = new FrameReassembler();
    private readonly Thread receiveThread;
    private readonly Thread keepAliveThread;

    private volatile bool disposed;
    private long lastHeardMs = -1;
    private long lastKeyframeRequestMs = -KeyframeRequestGapMs;
    private uint currentFormatId;
    private int inputSequence;

    // Host clock minus guest clock, from the latest ping, in ms.
    private double hostClockOffsetMs;
    private bool hasClockOffset;

    public bool IsConnected { get; private set; }
    public double RoundTripMs { get; private set; }
    public double LastNetworkDelayMs { get; private set; }
    public long BytesReceived { get; private set; }
    public long FramesLost => reassembler.FramesLost;
    public long FramesReceived => reassembler.FramesDelivered;

    public event Action<StreamVideoFormat> FormatChanged;
    // Frame bytes, keyframe, host clock when sent.
    public event Action<byte[], bool, uint> FrameReceived;
    public event Action<string> Disconnected;
    public delegate void AudioHandler(uint sequence, ReadOnlySpan<byte> opusFrame);
    // Raised on the receive thread. The data is only valid during the call.
    public event AudioHandler AudioReceived;

    // guestId: the id the host knows this guest by, when a lobby arranged the stream.
    public StreamGuestSession(IPEndPoint host, Action<string> log, uint? guestId = null)
    {
        this.guestId = guestId ?? (uint)Random.Shared.Next();
        this.host = host;
        this.log = log;
        socket = new Socket(host.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        // A keyframe arrives as a burst of a few hundred packets.
        socket.ReceiveBufferSize = 8 * 1024 * 1024;
        StreamHostSession.IgnoreConnectionResets(socket);
        socket.Connect(host);

        reassembler.FrameCompleted += OnFrameCompleted;

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true, Name = "Stream guest receive" };
        keepAliveThread = new Thread(KeepAliveLoop) { IsBackground = true, Name = "Stream guest keep-alive" };
    }

    // Separate from the constructor so event handlers are attached before the first packet.
    public void Start()
    {
        receiveThread.Start();
        keepAliveThread.Start();
        log?.Invoke($"[Stream] joining {host}.");
    }

    // Converts a host clock reading to how long ago it was on this machine's clock.
    public double AgeOfHostTime(uint hostTimeMs)
    {
        return hasClockOffset ? clock.ElapsedMilliseconds + hostClockOffsetMs - hostTimeMs : double.NaN;
    }

    private void KeepAliveLoop()
    {
        byte[] hello = new byte[StreamProtocol.HelloSize];
        StreamProtocol.WriteHeader(hello, StreamPacketType.Hello);
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(4), guestId);
        byte[] ping = new byte[StreamProtocol.PingSize];
        StreamProtocol.WriteHeader(ping, StreamPacketType.Ping);

        while (!disposed)
        {
            Send(hello);

            if (IsConnected)
            {
                BinaryPrimitives.WriteInt64LittleEndian(ping.AsSpan(4), clock.ElapsedTicks);
                Send(ping);

                if (clock.ElapsedMilliseconds - Interlocked.Read(ref lastHeardMs) > SilenceTimeoutMs)
                {
                    IsConnected = false;
                    log?.Invoke("[Stream] the host stopped answering.");
                    Disconnected?.Invoke("The host stopped answering.");
                }
            }

            Thread.Sleep(IsConnected ? KeepAliveMs : HelloWhileWaitingMs);
        }
    }

    private void ReceiveLoop()
    {
        byte[] buffer = new byte[2048];

        while (!disposed)
        {
            int length;

            try
            {
                length = socket.Receive(buffer);
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

            BytesReceived += length;
            StallForTest();
            Interlocked.Exchange(ref lastHeardMs, clock.ElapsedMilliseconds);
            HandlePacket(type, buffer, length);
        }
    }

    // For tests of an uneven connection: when ROMBOX_TEST_STALL_MS is set, the receive thread
    // stops for up to that long about five times a second, so packets pile up and then arrive
    // together, the way they do over a phone's hotspot.
    private static readonly int TestStallMs = int.TryParse(Environment.GetEnvironmentVariable("ROMBOX_TEST_STALL_MS"), out int stall) ? stall : 0;
    private long nextTestStallMs;

    private void StallForTest()
    {
        if (TestStallMs <= 0 || clock.ElapsedMilliseconds < nextTestStallMs)
        {
            return;
        }

        Thread.Sleep(Random.Shared.Next(TestStallMs / 3, TestStallMs + 1));
        nextTestStallMs = clock.ElapsedMilliseconds + Random.Shared.Next(100, 300);
    }

    private void HandlePacket(StreamPacketType type, byte[] buffer, int length)
    {
        switch (type)
        {
            case StreamPacketType.Welcome when length >= StreamProtocol.HelloSize:
                if (BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)) == guestId && !IsConnected)
                {
                    IsConnected = true;
                    log?.Invoke($"[Stream] connected to {host}.");
                }
                break;
            case StreamPacketType.VideoFormat:
                if (StreamProtocol.TryReadVideoFormat(buffer.AsSpan(0, length), out StreamVideoFormat format) && format.FormatId != currentFormatId)
                {
                    currentFormatId = format.FormatId;
                    reassembler.Reset();
                    log?.Invoke($"[Stream] video is {format.Width}x{format.Height} at {format.FrameRate} fps.");
                    FormatChanged?.Invoke(format);
                }
                break;
            case StreamPacketType.VideoChunk:
                if (currentFormatId != 0 && StreamProtocol.TryReadVideoChunk(buffer.AsSpan(0, length), out VideoChunk chunk))
                {
                    reassembler.AddChunk(chunk, buffer);
                }
                break;
            case StreamPacketType.Audio when length > StreamProtocol.AudioHeaderSize:
                AudioReceived?.Invoke(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(4)), buffer.AsSpan(StreamProtocol.AudioHeaderSize, length - StreamProtocol.AudioHeaderSize));
                break;
            case StreamPacketType.Pong when length >= StreamProtocol.PongSize:
                HandlePong(buffer);
                break;
            case StreamPacketType.Bye:
                IsConnected = false;
                log?.Invoke("[Stream] the host ended the stream.");
                Disconnected?.Invoke("The host ended the stream.");
                break;
        }
    }

    private void HandlePong(byte[] buffer)
    {
        long sentTicks = BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(4));
        uint hostTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(12));
        double roundTripMs = (clock.ElapsedTicks - sentTicks) * 1000.0 / Stopwatch.Frequency;
        double sentMs = sentTicks * 1000.0 / Stopwatch.Frequency;

        RoundTripMs = roundTripMs;
        // The host read its clock about half a round trip after the ping left.
        hostClockOffsetMs = hostTimeMs - (sentMs + roundTripMs / 2);
        hasClockOffset = true;
    }

    private void OnFrameCompleted(byte[] frame, bool isKeyframe, uint hostTimeMs, bool framesSkipped)
    {
        if (framesSkipped)
        {
            RequestKeyframe();
        }

        LastNetworkDelayMs = AgeOfHostTime(hostTimeMs);
        FrameReceived?.Invoke(frame, isKeyframe, hostTimeMs);
    }

    public void RequestKeyframe()
    {
        long now = clock.ElapsedMilliseconds;

        if (now - lastKeyframeRequestMs < KeyframeRequestGapMs)
        {
            return;
        }

        lastKeyframeRequestMs = now;
        byte[] request = new byte[StreamProtocol.HeaderSize];
        StreamProtocol.WriteHeader(request, StreamPacketType.KeyframeRequest);
        Send(request);
    }

    // Sends the whole state of one controller. Safe to call from any thread.
    public void SendPadState(int padIndex, bool isConnected, in StreamPadState state)
    {
        if (!IsConnected)
        {
            return;
        }

        uint sequence = (uint)Interlocked.Increment(ref inputSequence);
        Send(StreamProtocol.BuildGuestInput(sequence, padIndex, isConnected, state));
    }

    private void Send(byte[] packet)
    {
        try
        {
            socket.Send(packet);
        }
        catch (SocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        byte[] bye = new byte[StreamProtocol.HeaderSize];
        StreamProtocol.WriteHeader(bye, StreamPacketType.Bye);
        Send(bye);
        disposed = true;
        socket.Dispose();
    }
}
