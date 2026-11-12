using System;
using System.Collections.Generic;
using System.Linq;

// Collects a frame's UDP chunks and hands over each frame once all of them have arrived. Frames
// come out in order. A frame overtaken by a newer complete one is counted as lost.
public sealed class FrameReassembler
{
    private const int MaximumFramesInFlight = 32;

    private sealed class PartialFrame
    {
        public byte[][] Chunks;
        public int Received;
        public int TotalLength;
        public bool IsKeyframe;
        public uint HostTimeMs;
    }

    private readonly Dictionary<uint, PartialFrame> partialFrames = new Dictionary<uint, PartialFrame>();
    private uint lastDeliveredFrameId;
    private bool hasDelivered;

    public long FramesLost { get; private set; }
    public long FramesDelivered { get; private set; }

    // Called with the frame bytes, whether it is a keyframe, the host's clock when it was sent,
    // and whether frames were lost just before it.
    public event Action<byte[], bool, uint, bool> FrameCompleted;

    public void Reset()
    {
        partialFrames.Clear();
        hasDelivered = false;
    }

    public void AddChunk(in VideoChunk chunk, byte[] packet)
    {
        if (hasDelivered && !IsNewer(chunk.FrameId, lastDeliveredFrameId))
        {
            return;
        }

        if (!partialFrames.TryGetValue(chunk.FrameId, out PartialFrame frame))
        {
            frame = new PartialFrame
            {
                Chunks = new byte[chunk.ChunkCount][],
                IsKeyframe = chunk.IsKeyframe,
                HostTimeMs = chunk.HostTimeMs
            };
            partialFrames[chunk.FrameId] = frame;
            DropOldestIfCrowded();
        }

        if (chunk.ChunkIndex >= frame.Chunks.Length || frame.Chunks[chunk.ChunkIndex] != null)
        {
            return;
        }

        frame.Chunks[chunk.ChunkIndex] = packet.AsSpan(chunk.PayloadOffset, chunk.PayloadLength).ToArray();
        frame.Received++;
        frame.TotalLength += chunk.PayloadLength;

        if (frame.Received == frame.Chunks.Length)
        {
            Deliver(chunk.FrameId, frame);
        }
    }

    private void Deliver(uint frameId, PartialFrame frame)
    {
        bool framesSkipped = hasDelivered && frameId != lastDeliveredFrameId + 1;

        if (framesSkipped)
        {
            FramesLost += frameId - lastDeliveredFrameId - 1;
        }

        // Anything older than this frame can no longer be shown.
        foreach (uint staleId in partialFrames.Keys.Where(id => !IsNewer(id, frameId)).ToList())
        {
            partialFrames.Remove(staleId);
        }

        byte[] data = new byte[frame.TotalLength];
        int offset = 0;

        foreach (byte[] piece in frame.Chunks)
        {
            Buffer.BlockCopy(piece, 0, data, offset, piece.Length);
            offset += piece.Length;
        }

        lastDeliveredFrameId = frameId;
        hasDelivered = true;
        FramesDelivered++;
        FrameCompleted?.Invoke(data, frame.IsKeyframe, frame.HostTimeMs, framesSkipped);
    }

    private void DropOldestIfCrowded()
    {
        while (partialFrames.Count > MaximumFramesInFlight)
        {
            uint oldest = partialFrames.Keys.Aggregate((a, b) => IsNewer(a, b) ? b : a);
            partialFrames.Remove(oldest);
        }
    }

    // Frame ids wrap around after four billion frames.
    private static bool IsNewer(uint candidate, uint reference)
    {
        return (int)(candidate - reference) > 0;
    }
}
