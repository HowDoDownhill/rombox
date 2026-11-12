using Godot;
using System;

// The host's side of a stream: accepts guests and gives them controllers as soon as it joins the
// tree, then captures a window's picture and sound once StartCapture is called. Everything stops
// when the node is freed.
public partial class StreamHostNode : Node
{
    private readonly int port;
    private readonly StreamCaptureTarget initialTarget;

    private StreamHostSession session;
    private StreamEncoderProcess encoder;
    private StreamHostPads pads;
    private IStreamAudioCapture audio;

    // With a target the capture starts straight away; without one, call StartCapture later.
    public StreamHostNode(int port, StreamCaptureTarget target = null)
    {
        this.port = port;
        initialTarget = target;
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

        encoder = new StreamEncoderProcess(ffmpegPath, target, new StreamEncoderSettings(), Log);
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
