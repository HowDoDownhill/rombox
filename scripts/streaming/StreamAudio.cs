using Concentus;
using Concentus.Enums;
using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// Stream audio is always 48 kHz stereo Opus in 10 ms frames, so it needs no format packet.
public static class StreamAudioFormat
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int FrameSamples = 480;
    public const int FrameMs = 10;
    public const int BitrateBps = 128000;
}

// Captures what the game plays on the host, as interleaved 16-bit stereo at 48 kHz.
public interface IStreamAudioCapture : IDisposable
{
    // The buffer and how many of its values are valid. The buffer is reused after the call returns.
    event Action<short[], int> SamplesCaptured;
    bool Start();
}

public static class StreamAudioCaptures
{
    public static IStreamAudioCapture Create(StreamCaptureTarget target, string ffmpegPath, Action<string> log)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateWindows(target, log);
        }

        if (OperatingSystem.IsLinux())
        {
            return new PulseAudioCapture(ffmpegPath, target?.ProcessId ?? 0, log);
        }

        log?.Invoke("[Stream] game audio is not captured on this system yet.");
        return null;
    }

    // Kept out of Create so the Windows audio interfaces are only loaded on Windows.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IStreamAudioCapture CreateWindows(StreamCaptureTarget target, Action<string> log)
    {
        return new WindowsProcessAudioCapture(target, log);
    }
}

// Cuts captured audio into 10 ms frames and compresses each one with Opus.
public sealed class StreamAudioEncoder
{
    private readonly IOpusEncoder encoder;
    private readonly short[] frame = new short[StreamAudioFormat.FrameSamples * StreamAudioFormat.Channels];
    private readonly byte[] output = new byte[1000];
    private int filled;

    public event Action<byte[]> FrameEncoded;

    public StreamAudioEncoder()
    {
        encoder = OpusCodecFactory.CreateEncoder(StreamAudioFormat.SampleRate, StreamAudioFormat.Channels, OpusApplication.OPUS_APPLICATION_RESTRICTED_LOWDELAY);
        encoder.Bitrate = StreamAudioFormat.BitrateBps;
    }

    public void Add(short[] samples, int count)
    {
        int position = 0;

        while (position < count)
        {
            int copied = Math.Min(frame.Length - filled, count - position);
            Array.Copy(samples, position, frame, filled, copied);
            filled += copied;
            position += copied;

            if (filled == frame.Length)
            {
                filled = 0;
                int length = encoder.Encode(frame, StreamAudioFormat.FrameSamples, output, output.Length);

                if (length > 0)
                {
                    FrameEncoded?.Invoke(output.AsSpan(0, length).ToArray());
                }
            }
        }
    }
}

// Turns Opus packets back into sound. A few missing packets are filled in by Opus's own
// concealment; a packet that arrives after a later one is dropped.
public sealed class StreamAudioDecoder
{
    private const int LongestConcealedGap = 5;

    private readonly IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(StreamAudioFormat.SampleRate, StreamAudioFormat.Channels);
    private readonly ConcurrentQueue<short[]> frames = new ConcurrentQueue<short[]>();
    private readonly ConcurrentBag<short[]> spareFrames = new ConcurrentBag<short[]>();
    private uint lastSequence;
    private bool hasSequence;

    public int PendingFrames => frames.Count;
    public long PacketsLost { get; private set; }

    public void Receive(uint sequence, ReadOnlySpan<byte> packet)
    {
        if (hasSequence)
        {
            int gap = (int)(sequence - lastSequence);

            if (gap <= 0)
            {
                return;
            }

            PacketsLost += gap - 1;

            for (int missing = 1; missing < gap && gap - 1 <= LongestConcealedGap; missing++)
            {
                Decode(ReadOnlySpan<byte>.Empty);
            }
        }

        lastSequence = sequence;
        hasSequence = true;
        Decode(packet);
    }

    private void Decode(ReadOnlySpan<byte> packet)
    {
        if (!spareFrames.TryTake(out short[] frame))
        {
            frame = new short[StreamAudioFormat.FrameSamples * StreamAudioFormat.Channels];
        }

        try
        {
            if (decoder.Decode(packet, frame, StreamAudioFormat.FrameSamples) == StreamAudioFormat.FrameSamples)
            {
                frames.Enqueue(frame);
                return;
            }
        }
        catch (OpusException)
        {
        }

        spareFrames.Add(frame);
    }

    public bool TryTake(out short[] frame)
    {
        return frames.TryDequeue(out frame);
    }

    public void Recycle(short[] frame)
    {
        spareFrames.Add(frame);
    }
}
