using Godot;
using System;
using System.Diagnostics;
using System.Threading;

// Plays the host's sound on the guest. Packets are decoded on the network thread and handed to
// Godot's audio output each pass of the main loop. How much sound is kept in hand follows how
// unevenly packets have been arriving: little on a steady connection, more on a phone's, where a
// 70 ms pause in arrival would otherwise be a gap in the sound.
public sealed class StreamGuestAudio
{
    // Godot's output takes sound in pieces of about 20 ms, so less than this in hand runs dry
    // even when packets arrive perfectly evenly.
    private const int LowestLevelMs = 35;
    private const int HighestLevelMs = 160;
    // The level moves this much per adjustment, so the delay never jumps audibly.
    private const int LevelStepMs = 5;

    // Arrival is judged over about five seconds of packets. A pause longer than the limit is the
    // game being quiet (a Windows host sends nothing during silence) and is left out.
    private const int WindowPackets = 500;
    private const int AdjustEveryPackets = 50;
    private const double SilenceGapMs = 250;
    private const double CoveredShare = 0.98;
    private const long StillArrivingMs = 150;

    private readonly StreamAudioDecoder decoder = new StreamAudioDecoder();
    private readonly AudioStreamPlayer player;
    private readonly AudioStreamGeneratorPlayback playback;
    private readonly Vector2[] frame = new Vector2[StreamAudioFormat.FrameSamples];
    private readonly int capacityFrames;
    private bool waitingForStartLevel = true;

    private readonly double[] arrivalGapsMs = new double[WindowPackets];
    private readonly double[] sortedGapsMs = new double[WindowPackets];
    private int gapCount;
    private int nextGap;
    private int packetsSinceAdjust;
    private long lastPacketTicks;
    private volatile int levelMs = LowestLevelMs + 15;

    private float peakThisSecond;
    private long peakResetAtMs;

    public int BufferedMs { get; private set; }
    // How much sound is kept in hand right now, in ms.
    public int LevelMs => levelMs;
    public long FramesSkipped { get; private set; }
    // Times the output ran out while sound was still arriving. Running out because the game
    // went quiet does not count.
    public long TimesRunDry { get; private set; }
    public long FramesPlayed { get; private set; }
    public long PacketsLost => decoder.PacketsLost;
    // Loudest sample of the last second's sound, from 0 to 1, for tests.
    public float PeakLevel { get; private set; }

    public StreamGuestAudio(Node parent)
    {
        player = new AudioStreamPlayer
        {
            Stream = new AudioStreamGenerator { MixRate = StreamAudioFormat.SampleRate, BufferLength = 0.25f }
        };
        parent.AddChild(player);
        player.Play();
        playback = (AudioStreamGeneratorPlayback)player.GetStreamPlayback();
        capacityFrames = playback.GetFramesAvailable();
    }

    // Runs on the network thread.
    public void Receive(uint sequence, ReadOnlySpan<byte> packet)
    {
        long now = Stopwatch.GetTimestamp();
        long previous = Interlocked.Exchange(ref lastPacketTicks, now);

        if (previous != 0)
        {
            double gapMs = (now - previous) * 1000.0 / Stopwatch.Frequency;

            if (gapMs < SilenceGapMs)
            {
                arrivalGapsMs[nextGap] = gapMs;
                nextGap = (nextGap + 1) % WindowPackets;
                gapCount = Math.Min(gapCount + 1, WindowPackets);

                if (++packetsSinceAdjust >= AdjustEveryPackets)
                {
                    packetsSinceAdjust = 0;
                    AdjustLevel();
                }
            }
        }

        decoder.Receive(sequence, packet);
    }

    // Enough in hand to play through all but the longest pauses of the last few seconds.
    private void AdjustLevel()
    {
        Array.Copy(arrivalGapsMs, sortedGapsMs, gapCount);
        Array.Sort(sortedGapsMs, 0, gapCount);
        double longPause = sortedGapsMs[(int)((gapCount - 1) * CoveredShare)];
        int target = (int)Math.Clamp(LowestLevelMs + longPause - StreamAudioFormat.FrameMs, LowestLevelMs, HighestLevelMs);
        levelMs += Math.Clamp(target - levelMs, -LevelStepMs, LevelStepMs);
    }

    public void Update()
    {
        int level = levelMs;
        int queuedSamples = capacityFrames - playback.GetFramesAvailable();
        int pendingFrames = decoder.PendingFrames;
        BufferedMs = queuedSamples * 1000 / StreamAudioFormat.SampleRate + pendingFrames * StreamAudioFormat.FrameMs;

        if (queuedSamples == 0 && !waitingForStartLevel)
        {
            waitingForStartLevel = true;
            long sinceLastPacketMs = (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastPacketTicks)) * 1000 / Stopwatch.Frequency;

            if (sinceLastPacketMs < StillArrivingMs)
            {
                TimesRunDry++;
            }
        }

        // After running dry, playback waits for most of the level so it does not stutter.
        if (waitingForStartLevel)
        {
            if (BufferedMs < level * 3 / 4)
            {
                return;
            }

            waitingForStartLevel = false;
        }

        // Sound beyond the level waits outside the output, where it can still be skipped. A whole
        // extra level's worth waiting means the guest has fallen behind; the oldest is dropped
        // so the delay cannot keep growing.
        int mostWaitingFrames = Math.Max(level / StreamAudioFormat.FrameMs, 4);

        if (pendingFrames > mostWaitingFrames)
        {
            while (decoder.PendingFrames > mostWaitingFrames / 2 && decoder.TryTake(out short[] skipped))
            {
                decoder.Recycle(skipped);
                FramesSkipped++;
            }
        }

        int outputMs = queuedSamples * 1000 / StreamAudioFormat.SampleRate;

        while (outputMs < level && playback.CanPushBuffer(StreamAudioFormat.FrameSamples) && decoder.TryTake(out short[] samples))
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
