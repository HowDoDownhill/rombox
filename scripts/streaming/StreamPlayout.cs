using System;
using System.Collections.Generic;

// Decides when the guest shows each decoded frame. A network delivers frames unevenly: nothing for
// 40 ms, then three at once. Shown as they arrive, that is a stutter followed by skipped frames.
// The playout holds each frame until the host's own spacing has been restored, waiting only as
// long as the network has recently been uneven: a few milliseconds on a steady connection, more
// on a phone's.
public sealed class StreamPlayout
{
    // How far back the unevenness is measured, in frames (about five seconds).
    private const int WindowFrames = 300;
    // The wait is set to cover this share of recent frames; the few that arrive later are shown late.
    private const double CoveredShare = 0.95;
    private const double LongestWaitMs = 80;
    // The wait moves this much per adjustment, so a change in it never shows as a jump.
    private const double WaitStepMs = 1.0;
    private const int AdjustEveryFrames = 15;
    private const int MostFramesHeld = 8;

    private readonly Queue<(DecodedFrame Frame, double DueMs)> held = new Queue<(DecodedFrame, double)>();
    private readonly double[] transitMs = new double[WindowFrames];
    private readonly double[] sorted = new double[WindowFrames];
    private readonly object gate = new object();
    private int samples;
    private int nextSample;
    private double shortestTransitMs;
    private int framesSinceAdjust;

    // Off: every frame is shown as soon as it arrives, with no wait added.
    public bool IsSmoothing { get; set; } = true;

    // The wait currently added to every frame, in ms.
    public double WaitMs { get; private set; }
    // Frames passed over because a newer one was already due.
    public long FramesSkipped { get; private set; }

    public void Reset()
    {
        lock (gate)
        {
            held.Clear();
            samples = 0;
            nextSample = 0;
            WaitMs = 0;
        }
    }

    // Called when a frame has been decoded. arrivedMs is this machine's clock; the frame carries
    // the host's clock from when it was sent. The two clocks need not agree: only how the
    // difference changes from frame to frame matters.
    public void Add(DecodedFrame frame, double arrivedMs, Action<DecodedFrame> recycle)
    {
        lock (gate)
        {
            double transit = arrivedMs - frame.HostTimeMs;
            transitMs[nextSample] = transit;
            nextSample = (nextSample + 1) % WindowFrames;
            samples = Math.Min(samples + 1, WindowFrames);

            if (++framesSinceAdjust >= AdjustEveryFrames || samples < AdjustEveryFrames)
            {
                framesSinceAdjust = 0;
                Adjust();
            }

            held.Enqueue((frame, IsSmoothing ? frame.HostTimeMs + shortestTransitMs + WaitMs : 0));

            // Falling this far behind means the estimate is off; catch up instead of lagging.
            while (held.Count > MostFramesHeld)
            {
                recycle(held.Dequeue().Frame);
                FramesSkipped++;
            }
        }
    }

    // The frame to show now, or null. When several are due only the newest is returned.
    public DecodedFrame TakeDue(double nowMs, Action<DecodedFrame> recycle)
    {
        lock (gate)
        {
            DecodedFrame due = null;

            while (held.Count > 0 && held.Peek().DueMs <= nowMs)
            {
                if (due != null)
                {
                    recycle(due);
                    FramesSkipped++;
                }

                due = held.Dequeue().Frame;
            }

            return due;
        }
    }

    private void Adjust()
    {
        Array.Copy(transitMs, sorted, samples);
        Array.Sort(sorted, 0, samples);
        shortestTransitMs = sorted[0];
        double target = IsSmoothing ? Math.Min(sorted[(int)((samples - 1) * CoveredShare)] - shortestTransitMs, LongestWaitMs) : 0;
        WaitMs += Math.Clamp(target - WaitMs, -WaitStepMs, WaitStepMs);
    }
}
