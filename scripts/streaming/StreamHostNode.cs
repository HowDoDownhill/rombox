using Godot;
using System;

// The host's side of a stream: accepts guests and gives them controllers as soon as it joins the
// tree, then captures a window's picture and sound once StartCapture is called. Everything stops
// when the node is freed.
public partial class StreamHostNode : Node
{
    private const double StatsEverySeconds = 5.0;

    private readonly int port;
    private readonly StreamCaptureTarget initialTarget;
    private readonly StreamEncoderSettings encoderSettings;

    private StreamHostSession session;
    private StreamEncoderProcess encoder;
    private StreamHostPads pads;
    private IStreamAudioCapture audio;

    private double secondsSinceStats;
    private long framesAtLastStats;
    private long keyframesAtLastStats;
    private long bytesAtLastStats;
    private long audioFramesAtLastStats;

    // With a target the capture starts straight away; without one, call StartCapture later.
    // encoderSettings: bitrate, picture size and shape, or null for the defaults.
    public StreamHostNode(int port, StreamCaptureTarget target = null, StreamEncoderSettings encoderSettings = null)
    {
        this.port = port;
        initialTarget = target;
        this.encoderSettings = encoderSettings ?? new StreamEncoderSettings();
    }

    public int Port => session?.Port ?? port;
    public bool IsCapturing => encoder != null;

    public override void _Ready()
    {
        try
        {
            session = new StreamHostSession(port, Log);
        }
        catch (Exception failure)
        {
            GD.PrintErr($"[Stream] could not open UDP port {port}: {failure.Message}");
            return;
        }

        pads = new StreamHostPads(session, StreamVirtualPadBackends.Create(Log), Log);

        if (initialTarget != null)
        {
            StartCapture(initialTarget);
        }
    }

    // Plugs in controllers for a guest before it connects, so a game launched next finds them.
    // Only reserved controllers are used from then on.
    public int ReservePads(uint guestId, int padCount)
    {
        if (pads == null)
        {
            return 0;
        }

        pads.AllowsUnreservedPads = false;
        return pads.Reserve(guestId, padCount);
    }

    public bool StartCapture(StreamCaptureTarget target)
    {
        if (session == null || encoder != null)
        {
            return false;
        }

        string appDirectory = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
        string ffmpegPath = FfmpegTool.FindExecutable(appDirectory);

        if (ffmpegPath == null)
        {
            GD.PrintErr($"[Stream] ffmpeg was not found. Put it in tools/ffmpeg or set {FfmpegTool.OverrideVariable}.");
            return false;
        }

        encoder = new StreamEncoderProcess(ffmpegPath, target, encoderSettings, Log);
        encoder.FormatReady += format => session?.SetVideoFormat(format);
        encoder.FrameEncoded += (frame, isKeyframe) => session?.SendFrame(frame, isKeyframe);

        if (!encoder.Start())
        {
            GD.PrintErr("[Stream] capture did not start.");
            return false;
        }

        audio = StreamAudioCaptures.Create(target, ffmpegPath, Log);

        if (audio != null)
        {
            var audioEncoder = new StreamAudioEncoder();
            audioEncoder.FrameEncoded += frame => session?.SendAudio(frame);
            audio.SamplesCaptured += audioEncoder.Add;
            audio.Start();
        }

        return true;
    }

    // Every few seconds the log says what the host sent, so a guest's dip in frame rate can be
    // traced to the host or to the network between them.
    public override void _Process(double delta)
    {
        secondsSinceStats += delta;

        if (session == null || encoder == null || secondsSinceStats < StatsEverySeconds)
        {
            return;
        }

        long frames = session.FramesSent;
        long keyframes = session.KeyframesSent;
        long bytes = session.BytesSent;
        long audioFrames = session.AudioFramesSent;
        int guests = session.GuestCount;

        // Frames are counted whether or not a guest is there to receive them; bytes only when sent.
        double framesPerSecond = (frames - framesAtLastStats) / secondsSinceStats;
        double megabitsPerSecond = (bytes - bytesAtLastStats) * 8 / 1_000_000.0 / secondsSinceStats;
        double megabitsPerGuest = guests > 0 ? megabitsPerSecond / guests : 0;
        GD.Print($"[Stream] host: {framesPerSecond:0.0} fps captured, {keyframes - keyframesAtLastStats} keyframes, {(audioFrames - audioFramesAtLastStats) / secondsSinceStats:0} sound frames/s, {megabitsPerGuest:0.0} Mbit/s to each of {guests} guest(s), {pads?.PadCount ?? 0} guest controller(s).");

        framesAtLastStats = frames;
        keyframesAtLastStats = keyframes;
        bytesAtLastStats = bytes;
        audioFramesAtLastStats = audioFrames;
        secondsSinceStats = 0;
    }

    private static void Log(string line)
    {
        GD.Print(line);
    }

    public override void _ExitTree()
    {
        audio?.Dispose();
        audio = null;
        encoder?.Dispose();
        encoder = null;
        pads?.Dispose();
        pads = null;
        session?.Dispose();
        session = null;
    }
}
