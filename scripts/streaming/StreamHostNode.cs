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
    private StreamBrowserGuests browserGuests;
    private bool ownsBrowserGuests;

    private const double WindowLookupEverySeconds = 1.0;
    private string ffmpegPath;
    private StreamCaptureTarget captureTarget;
    private System.Threading.Tasks.Task<X11Windows.Found?> windowLookup;
    private double secondsSinceWindowLookup;
    private X11Windows.Found? changedWindow;

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

    // Guests who joined from a web page before the stream existed, as a lobby collects them.
    // Set before the node joins the tree. Left unset, the node accepts browsers by itself.
    public StreamBrowserGuests BrowserGuests { get; set; }
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
        StartBrowserGuests();

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

    private void StartBrowserGuests()
    {
        browserGuests = BrowserGuests;

        if (browserGuests == null && StreamBrowserGuests.IsSupported)
        {
            try
            {
                browserGuests = new StreamBrowserGuests(session.Port + StreamProtocol.BrowserPortOffset, Log);
                ownsBrowserGuests = true;
                Log($"[Stream] browser join code: {browserGuests.JoinCode}");
            }
            catch (Exception failure)
            {
                Log($"[Stream] browsers cannot join a game hosted here: {failure.Message}");
            }
        }

        if (browserGuests == null)
        {
            Log("[Stream] browsers cannot join a game hosted here. That needs Windows 11, or libmsquic on Linux.");
            return;
        }

        browserGuests.Attach(session);
    }

    public bool StartCapture(StreamCaptureTarget target)
    {
        if (session == null || encoder != null)
        {
            return false;
        }

        string appDirectory = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
        ffmpegPath = FfmpegTool.FindExecutable(appDirectory);

        if (ffmpegPath == null)
        {
            GD.PrintErr($"[Stream] ffmpeg was not found. Put it in tools/ffmpeg or set {FfmpegTool.OverrideVariable}.");
            return false;
        }

        if (!StartVideo(target))
        {
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

    private bool StartVideo(StreamCaptureTarget target)
    {
        captureTarget = target;
        encoder = new StreamEncoderProcess(ffmpegPath, target, encoderSettings, Log);
        encoder.FormatReady += format => session?.SetVideoFormat(format);
        encoder.FrameEncoded += (frame, isKeyframe) => session?.SendFrame(frame, isKeyframe);

        if (!encoder.Start())
        {
            GD.PrintErr("[Stream] capture did not start.");
            encoder.Dispose();
            encoder = null;
            return false;
        }

        return true;
    }

    // Linux: an X11 capture is fixed to the window and size it started with. A game that goes
    // full screen, is resized, or opens its real window after a launcher would leave guests with
    // a frozen or cut-off picture. Once a second the game's window is looked up again, off the
    // main thread, and when it has settled on something new the capture starts over there.
    private void FollowWindow(double delta)
    {
        if (!OperatingSystem.IsLinux() || captureTarget == null || captureTarget.ProcessId <= 0 || session == null)
        {
            return;
        }

        if (windowLookup != null)
        {
            if (!windowLookup.IsCompleted)
            {
                return;
            }

            X11Windows.Found? found = windowLookup.IsCompletedSuccessfully ? windowLookup.Result : null;
            windowLookup = null;
            ApplyWindowLookup(found);
        }

        secondsSinceWindowLookup += delta;

        if (secondsSinceWindowLookup >= WindowLookupEverySeconds)
        {
            secondsSinceWindowLookup = 0;
            int processId = captureTarget.ProcessId;
            windowLookup = System.Threading.Tasks.Task.Run(() => X11Windows.FindForProcess(processId));
        }
    }

    private void ApplyWindowLookup(X11Windows.Found? found)
    {
        if (found == null)
        {
            changedWindow = null;
            return;
        }

        X11Windows.Found now = found.Value;

        if (now.WindowId == captureTarget.X11WindowId && now.Width == captureTarget.X11Width && now.Height == captureTarget.X11Height)
        {
            changedWindow = null;
            return;
        }

        // Two looks in a row have to agree, so a window being dragged to a new size is not
        // chased through every size on the way.
        bool hasSettled = changedWindow is X11Windows.Found before && before.WindowId == now.WindowId && before.Width == now.Width && before.Height == now.Height;
        changedWindow = now;

        if (!hasSettled)
        {
            return;
        }

        changedWindow = null;
        Log($"[Stream] the game's window is now {now.Width}x{now.Height}; starting the capture again.");
        encoder?.Dispose();
        encoder = null;
        StartVideo(new StreamCaptureTarget { X11WindowId = now.WindowId, X11Width = now.Width, X11Height = now.Height, ProcessId = captureTarget.ProcessId });
    }

    // Every few seconds the log says what the host sent, so a guest's dip in frame rate can be
    // traced to the host or to the network between them.
    public override void _Process(double delta)
    {
        FollowWindow(delta);
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
        // Closing the session tells every guest the game has ended, browsers included, so it
        // comes before they are let go.
        session?.Dispose();
        session = null;
        browserGuests?.Detach();

        if (ownsBrowserGuests)
        {
            browserGuests.Dispose();
        }

        browserGuests = null;
    }
}
