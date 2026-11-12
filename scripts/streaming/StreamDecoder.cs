using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

// One decoded picture in I420: a full-size brightness plane and two quarter-size colour planes.
public sealed class DecodedFrame
{
    public int Width;
    public int Height;
    public byte[] Luma;
    public byte[] ChromaBlue;
    public byte[] ChromaRed;
    public bool IsFullRange;
    public long DecodedAtTicks;
    public uint HostTimeMs;
}

// Decodes H.264 inside RomBox through FFmpeg's libavcodec. Each frame comes back from the call
// that sent it, with nothing held back for later frames.
public sealed class StreamDecoder : IDisposable
{
    private static readonly byte[] StartCode = { 0, 0, 0, 1 };

    private readonly StreamVideoFormat format;
    private readonly Action<string> log;
    private readonly ConcurrentBag<DecodedFrame> spareFrames = new ConcurrentBag<DecodedFrame>();
    private readonly byte[] parameterSets;
    private readonly int lengthSize;

    private IntPtr context;
    private IntPtr packet;
    private IntPtr frame;
    private bool waitingForKeyframe = true;
    private bool reportedFormatProblem;

    public long FramesDecoded { get; private set; }
    public long FramesSent { get; private set; }

    public event Action<DecodedFrame> FrameDecoded;

    public StreamDecoder(StreamVideoFormat format, Action<string> log)
    {
        this.format = format;
        this.log = log;
        (parameterSets, lengthSize) = ReadDecoderConfiguration(format.DecoderSetup);
    }

    public bool Start(string appDirectory = null)
    {
        if (!Libav.TryLoad(appDirectory, log))
        {
            return false;
        }

        IntPtr codec = Libav.FindDecoder(Libav.CodecIdH264);

        if (codec == IntPtr.Zero)
        {
            log?.Invoke("[Stream] this FFmpeg build has no H.264 decoder.");
            return false;
        }

        context = Libav.AllocContext(codec);
        // One thread: frame threading holds back a frame per extra thread.
        Libav.OptSet(context, "threads", "1", 0);
        Libav.OptSet(context, "flags", "+low_delay", 0);

        int result = Libav.Open(context, codec, IntPtr.Zero);

        if (result < 0)
        {
            log?.Invoke($"[Stream] the H.264 decoder did not open (error {result}).");
            return false;
        }

        packet = Libav.AllocPacket();
        frame = Libav.AllocFrame();
        log?.Invoke($"[Stream] decoding {format.Width}x{format.Height}.");
        return true;
    }

    // Decodes one frame and raises FrameDecoded before returning. Returns false while waiting for
    // the first keyframe, or when the decoder rejects the frame.
    public bool Decode(byte[] data, bool isKeyframe, uint hostTimeMs)
    {
        if (context == IntPtr.Zero || (waitingForKeyframe && !isKeyframe))
        {
            return false;
        }

        waitingForKeyframe = false;
        byte[] annexB = ToAnnexB(data, isKeyframe);

        if (Libav.NewPacket(packet, annexB.Length) < 0)
        {
            return false;
        }

        Marshal.Copy(annexB, 0, Marshal.ReadIntPtr(packet, Libav.PacketDataOffset), annexB.Length);
        int sendResult = Libav.SendPacket(context, packet);
        Libav.UnrefPacket(packet);
        FramesSent++;

        if (sendResult < 0 && sendResult != Libav.ErrorAgain)
        {
            return false;
        }

        while (Libav.ReceiveFrame(context, frame) == 0)
        {
            DecodedFrame picture = CopyFrame(hostTimeMs);
            Libav.UnrefFrame(frame);

            if (picture != null)
            {
                FramesDecoded++;
                FrameDecoded?.Invoke(picture);
            }
        }

        return true;
    }

    public void Recycle(DecodedFrame picture)
    {
        if (picture != null && picture.Width == format.Width && picture.Height == format.Height)
        {
            spareFrames.Add(picture);
        }
    }

    private DecodedFrame CopyFrame(uint hostTimeMs)
    {
        int width = Marshal.ReadInt32(frame, Libav.FrameWidthOffset);
        int height = Marshal.ReadInt32(frame, Libav.FrameHeightOffset);
        int pixelFormat = Marshal.ReadInt32(frame, Libav.FrameFormatOffset);

        if (width != format.Width || height != format.Height || (pixelFormat != Libav.PixelFormatYuv420p && pixelFormat != Libav.PixelFormatYuvj420p))
        {
            if (!reportedFormatProblem)
            {
                reportedFormatProblem = true;
                log?.Invoke($"[Stream] the decoder gave {width}x{height} in pixel format {pixelFormat}; expected {format.Width}x{format.Height} 4:2:0.");
            }

            return null;
        }

        int chromaWidth = width / 2;
        int chromaHeight = height / 2;

        if (!spareFrames.TryTake(out DecodedFrame picture))
        {
            picture = new DecodedFrame
            {
                Width = width,
                Height = height,
                Luma = new byte[width * height],
                ChromaBlue = new byte[chromaWidth * chromaHeight],
                ChromaRed = new byte[chromaWidth * chromaHeight]
            };
        }

        CopyPlane(0, width, height, picture.Luma);
        CopyPlane(1, chromaWidth, chromaHeight, picture.ChromaBlue);
        CopyPlane(2, chromaWidth, chromaHeight, picture.ChromaRed);
        picture.IsFullRange = pixelFormat == Libav.PixelFormatYuvj420p;
        picture.DecodedAtTicks = Stopwatch.GetTimestamp();
        picture.HostTimeMs = hostTimeMs;
        return picture;
    }

    private void CopyPlane(int plane, int width, int height, byte[] destination)
    {
        IntPtr source = Marshal.ReadIntPtr(frame, plane * IntPtr.Size);
        int stride = Marshal.ReadInt32(frame, Libav.FrameLinesizeOffset + plane * 4);

        if (stride == width)
        {
            Marshal.Copy(source, destination, 0, width * height);
            return;
        }

        for (int row = 0; row < height; row++)
        {
            Marshal.Copy(source + row * stride, destination, row * width, width);
        }
    }

    // The stream carries length-prefixed NAL units (as FLV and MP4 do). The decoder is given start
    // codes instead, with SPS and PPS in front of every keyframe so it never needs separate setup.
    private byte[] ToAnnexB(byte[] data, bool isKeyframe)
    {
        var output = new List<byte>(data.Length + (isKeyframe ? parameterSets.Length : 0) + 32);

        if (isKeyframe)
        {
            output.AddRange(parameterSets);
        }

        int position = 0;

        while (position + lengthSize <= data.Length)
        {
            int nalLength = 0;

            for (int i = 0; i < lengthSize; i++)
            {
                nalLength = (nalLength << 8) | data[position + i];
            }

            position += lengthSize;

            if (nalLength <= 0 || position + nalLength > data.Length)
            {
                break;
            }

            output.AddRange(StartCode);
            output.AddRange(new ArraySegment<byte>(data, position, nalLength));
            position += nalLength;
        }

        return output.ToArray();
    }

    // avcC: version, profile, compatibility, level, NAL length size, then the SPS and PPS lists.
    private static (byte[] ParameterSets, int LengthSize) ReadDecoderConfiguration(byte[] avcC)
    {
        var sets = new List<byte>();

        if (avcC == null || avcC.Length < 7)
        {
            return (sets.ToArray(), 4);
        }

        int lengthSize = (avcC[4] & 0x03) + 1;
        int position = 5;

        for (int list = 0; list < 2 && position < avcC.Length; list++)
        {
            int count = list == 0 ? avcC[position] & 0x1F : avcC[position];
            position++;

            for (int i = 0; i < count && position + 2 <= avcC.Length; i++)
            {
                int length = BinaryPrimitives.ReadUInt16BigEndian(avcC.AsSpan(position));
                position += 2;

                if (position + length > avcC.Length)
                {
                    break;
                }

                sets.AddRange(StartCode);
                sets.AddRange(new ArraySegment<byte>(avcC, position, length));
                position += length;
            }
        }

        return (sets.ToArray(), lengthSize);
    }

    public void Dispose()
    {
        if (frame != IntPtr.Zero) Libav.FreeFrame(ref frame);
        if (packet != IntPtr.Zero) Libav.FreePacket(ref packet);
        if (context != IntPtr.Zero) Libav.FreeContext(ref context);
    }
}
