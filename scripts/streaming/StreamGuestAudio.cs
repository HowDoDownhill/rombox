using Godot;
using System;

// Plays the host's sound on the guest. Packets are decoded on the network thread and handed to
// Godot's audio output once a frame.
public sealed class StreamGuestAudio
{
    // After running dry, playback waits for this much sound so it does not stutter.
    private const int StartLevelMs = 30;
    // Godot's output is topped up to this level each frame. It takes sound in pieces of about
    // 20 ms and a frame lasts 16 ms, so less than this runs dry.
    private const int OutputLevelMs = 50;
    // Sound beyond the output level waits here. More than this many frames means the guest has
    // fallen behind, and all but the newest are skipped so the delay cannot grow.
    private const int MostWaitingFrames = 4;

    private readonly StreamAudioDecoder decoder = new StreamAudioDecoder();
    private readonly AudioStreamPlayer player;
    private readonly AudioStreamGeneratorPlayback playback;
    private readonly Vector2[] frame = new Vector2[StreamAudioFormat.FrameSamples];
    private readonly int capacityFrames;
    private bool waitingForStartLevel = true;

    public int BufferedMs { get; private set; }
    public long FramesSkipped { get; private set; }
    public long TimesRunDry { get; private set; }
    public long FramesPlayed { get; private set; }
    public long PacketsLost => decoder.PacketsLost;
    // Loudest sample of the last second's sound, from 0 to 1, for tests.
    public float PeakLevel { get; private set; }
    private float peakThisSecond;
    private long peakResetAtMs;

    public StreamGuestAudio(Node parent)
    {
        player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = StreamAudioFormat.SampleRate, BufferLength = 0.2f }
        };
        parent.AddChild(player);
        player.Play();
        playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        capacityFrames = playback.GetFramesAvailable();
    }

    // Runs on the network thread.
    public void Receive(uint sequence, ReadOnlySpan<byte> packet)
    {
        decoder.Receive(sequence, packet);
    }

    public void Update()
    {
        int queuedSamples = capacityFrames - playback.GetFramesAvailable();
        int pendingFrames = decoder.PendingFrames;
        BufferedMs = queuedSamples * 1000 / StreamAudioFormat.SampleRate + pendingFrames * StreamAudioFormat.FrameMs;

        if (queuedSamples == 0 && !waitingForStartLevel)
        {
            waitingForStartLevel = true;
            TimesRunDry++;
        }

        if (waitingForStartLevel)
        {
            if (BufferedMs < StartLevelMs)
            {
                return;
            }

            waitingForStartLevel = false;
        }

        if (pendingFrames > MostWaitingFrames)
        {
            while (decoder.PendingFrames > 1 && decoder.TryTake(out short[] skipped))
            {
                decoder.Recycle(skipped);
                FramesSkipped++;
            }
        }

        int outputMs = queuedSamples * 1000 / StreamAudioFormat.SampleRate;

        while (outputMs < OutputLevelMs && playback.CanPushBuffer(StreamAudioFormat.FrameSamples) && decoder.TryTake(out short[] samples))
        {
            outputMs += StreamAudioFormat.FrameMs;
            for (int index = 0; index < frame.Length; index++)
            {
                float left = samples[index * 2] / 32768f;
                float right = samples[index * 2 + 1] / 32768f;
                frame[index] = new Vector2(left, right);
                peakThisSecond = Math.Max(peakThisSecond, Math.Max(Math.Abs(left), Math.Abs(right)));
            }

            playback.PushBuffer(frame);
            decoder.Recycle(samples);
            FramesPlayed++;
        }

        long now = (long)Time.GetTicksMsec();

        if (now - peakResetAtMs >= 1000)
        {
            PeakLevel = peakThisSecond;
            peakThisSecond = 0;
            peakResetAtMs = now;
        }
    }
}
