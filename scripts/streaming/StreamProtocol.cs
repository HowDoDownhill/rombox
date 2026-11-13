using System;
using System.Buffers.Binary;

// Packet layout shared by the stream host and its guests. Every UDP packet starts with "RB", a
// protocol version byte and a packet type byte. Numbers are little-endian.
public static class StreamProtocol
{
    public const int DefaultPort = 55450;
    // Guests in a web browser connect to the UDP port this far above the host's port.
    public const int BrowserPortOffset = 1;
    public const byte Version = 1;
    public const int HeaderSize = 4;

    // Small enough to cross the internet without IP fragmentation.
    public const int MaxChunkPayload = 1200;

    // frame id (4), chunk index (2), chunk count (2), flags (1), host time in ms (4)
    public const int VideoChunkHeaderSize = HeaderSize + 13;
    public const byte KeyframeFlag = 0x01;

    public const int HelloSize = HeaderSize + 4;
    public const int PingSize = HeaderSize + 8;
    public const int PongSize = HeaderSize + 12;

    // sequence (4), pad index (1), flags (1), buttons (2), triggers (1 + 1), four stick axes (2 each)
    public const int GuestInputSize = HeaderSize + 18;
    public const byte PadConnectedFlag = 0x01;
    public const int MaxPadsPerGuest = 4;

    // sequence (4), host time in ms (4), then one Opus frame
    public const int AudioHeaderSize = HeaderSize + 8;

    public static void WriteHeader(Span<byte> packet, StreamPacketType type)
    {
        packet[0] = (byte)'R';
        packet[1] = (byte)'B';
        packet[2] = Version;
        packet[3] = (byte)type;
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> packet, out StreamPacketType type)
    {
        type = default;

        if (packet.Length < HeaderSize || packet[0] != 'R' || packet[1] != 'B' || packet[2] != Version)
        {
            return false;
        }

        type = (StreamPacketType)packet[3];
        return true;
    }

    public static int WriteVideoChunkHeader(Span<byte> packet, uint frameId, ushort chunkIndex, ushort chunkCount, bool isKeyframe, uint hostTimeMs)
    {
        WriteHeader(packet, StreamPacketType.VideoChunk);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(4), frameId);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(8), chunkIndex);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.Slice(10), chunkCount);
        packet[12] = isKeyframe ? KeyframeFlag : (byte)0;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.Slice(13), hostTimeMs);
        return VideoChunkHeaderSize;
    }

    public static bool TryReadVideoChunk(ReadOnlySpan<byte> packet, out VideoChunk chunk)
    {
        chunk = default;

        if (packet.Length < VideoChunkHeaderSize)
        {
            return false;
        }

        chunk = new VideoChunk
        {
            FrameId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4)),
            ChunkIndex = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(8)),
            ChunkCount = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(10)),
            IsKeyframe = (packet[12] & KeyframeFlag) != 0,
            HostTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(13)),
            PayloadOffset = VideoChunkHeaderSize,
            PayloadLength = packet.Length - VideoChunkHeaderSize
        };

        return chunk.ChunkCount > 0 && chunk.ChunkIndex < chunk.ChunkCount;
    }

    public static byte[] BuildGuestInput(uint sequence, int padIndex, bool isConnected, in StreamPadState state)
    {
        byte[] packet = new byte[GuestInputSize];
        Span<byte> span = packet;
        WriteHeader(span, StreamPacketType.GuestInput);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), sequence);
        span[8] = (byte)padIndex;
        span[9] = isConnected ? PadConnectedFlag : (byte)0;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), (ushort)state.Buttons);
        span[12] = state.LeftTrigger;
        span[13] = state.RightTrigger;
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(14), state.LeftX);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(16), state.LeftY);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(18), state.RightX);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(20), state.RightY);
        return packet;
    }

    public static bool TryReadGuestInput(ReadOnlySpan<byte> packet, out uint sequence, out int padIndex, out bool isConnected, out StreamPadState state)
    {
        sequence = 0;
        padIndex = 0;
        isConnected = false;
        state = default;

        if (packet.Length < GuestInputSize)
        {
            return false;
        }

        sequence = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4));
        padIndex = packet[8];
        isConnected = (packet[9] & PadConnectedFlag) != 0;
        state = new StreamPadState
        {
            Buttons = (StreamPadButtons)BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(10)),
            LeftTrigger = packet[12],
            RightTrigger = packet[13],
            LeftX = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(14)),
            LeftY = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(16)),
            RightX = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(18)),
            RightY = BinaryPrimitives.ReadInt16LittleEndian(packet.Slice(20))
        };

        return padIndex < MaxPadsPerGuest;
    }

    // Format id (4), codec (1), width (2), height (2), frame rate (2), decoder setup length (2), decoder setup.
    public static byte[] BuildVideoFormat(StreamVideoFormat format)
    {
        byte[] packet = new byte[HeaderSize + 13 + format.DecoderSetup.Length];
        Span<byte> span = packet;
        WriteHeader(span, StreamPacketType.VideoFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), format.FormatId);
        span[8] = (byte)format.Codec;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(9), (ushort)format.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(11), (ushort)format.Height);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(13), (ushort)format.FrameRate);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(15), (ushort)format.DecoderSetup.Length);
        format.DecoderSetup.CopyTo(span.Slice(17));
        return packet;
    }

    public static bool TryReadVideoFormat(ReadOnlySpan<byte> packet, out StreamVideoFormat format)
    {
        format = null;

        if (packet.Length < HeaderSize + 13)
        {
            return false;
        }

        int setupLength = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(15));

        if (packet.Length < HeaderSize + 13 + setupLength)
        {
            return false;
        }

        format = new StreamVideoFormat
        {
            FormatId = BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(4)),
            Codec = (StreamVideoCodec)packet[8],
            Width = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(9)),
            Height = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(11)),
            FrameRate = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(13)),
            DecoderSetup = packet.Slice(17, setupLength).ToArray()
        };

        return format.Width > 0 && format.Height > 0;
    }
}

public enum StreamPacketType : byte
{
    // Guest to host, repeated as a keep-alive. Carries the guest's random id.
    Hello = 1,
    // Host to guest, answering Hello. Echoes the guest's id.
    Welcome = 2,
    // Host to guest: the codec, size and decoder setup. Repeated every second.
    VideoFormat = 3,
    VideoChunk = 4,
    // Guest to host after losing a frame.
    KeyframeRequest = 5,
    // Either side. Carries the sender's clock in ticks.
    Ping = 6,
    // Echoes the ping's ticks and adds the answering host's stream clock in ms.
    Pong = 7,
    Bye = 8,
    // Guest to host: the whole state of one of the guest's controllers. Sent on every change
    // and repeated, so a lost packet is corrected by the next one.
    GuestInput = 9,
    // Host to guest: one 10 ms Opus frame of the game's sound.
    Audio = 10
}

// The bit values match an Xbox 360 controller's report.
[Flags]
public enum StreamPadButtons : ushort
{
    None = 0,
    DpadUp = 0x0001,
    DpadDown = 0x0002,
    DpadLeft = 0x0004,
    DpadRight = 0x0008,
    Start = 0x0010,
    Back = 0x0020,
    LeftStick = 0x0040,
    RightStick = 0x0080,
    LeftShoulder = 0x0100,
    RightShoulder = 0x0200,
    Guide = 0x0400,
    A = 0x1000,
    B = 0x2000,
    X = 0x4000,
    Y = 0x8000
}

// One controller's state in Xbox 360 terms: sticks from -32768 to 32767 with up and right
// positive, triggers from 0 to 255.
public struct StreamPadState : IEquatable<StreamPadState>
{
    public StreamPadButtons Buttons;
    public byte LeftTrigger;
    public byte RightTrigger;
    public short LeftX;
    public short LeftY;
    public short RightX;
    public short RightY;

    public bool Equals(StreamPadState other)
    {
        return Buttons == other.Buttons && LeftTrigger == other.LeftTrigger && RightTrigger == other.RightTrigger
            && LeftX == other.LeftX && LeftY == other.LeftY && RightX == other.RightX && RightY == other.RightY;
    }

    public override bool Equals(object other) => other is StreamPadState state && Equals(state);
    public override int GetHashCode() => HashCode.Combine(Buttons, LeftTrigger, RightTrigger, LeftX, LeftY, RightX, RightY);
}

public enum StreamVideoCodec : byte
{
    H264 = 1
}

public struct VideoChunk
{
    public uint FrameId;
    public ushort ChunkIndex;
    public ushort ChunkCount;
    public bool IsKeyframe;
    public uint HostTimeMs;
    public int PayloadOffset;
    public int PayloadLength;
}

public sealed class StreamVideoFormat
{
    public uint FormatId;
    public StreamVideoCodec Codec = StreamVideoCodec.H264;
    public int Width;
    public int Height;
    public int FrameRate;

    // H.264: the avcC record (SPS and PPS) the decoder needs before the first frame.
    public byte[] DecoderSetup = Array.Empty<byte>();
}
