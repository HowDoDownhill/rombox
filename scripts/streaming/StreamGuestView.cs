using Godot;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;

// Full-screen view of a host's stream. Frames are decoded on the network thread; the newest one
// is uploaded as three single-channel textures each frame and turned into colour by a shader.
public partial class StreamGuestView : CanvasLayer
{
    private const string ShaderCode = @"
shader_type canvas_item;
uniform sampler2D chroma_blue : filter_linear;
uniform sampler2D chroma_red : filter_linear;
uniform bool full_range = false;

void fragment() {
    float y = texture(TEXTURE, UV).r;
    float cb = texture(chroma_blue, UV).r - 0.5;
    float cr = texture(chroma_red, UV).r - 0.5;
    if (!full_range) {
        y = (y - 16.0 / 255.0) * (255.0 / 219.0);
        cb *= 255.0 / 224.0;
        cr *= 255.0 / 224.0;
    }
    // BT.601, which hardware encoders use when they convert a captured RGB window.
    vec3 rgb = vec3(y + 1.402 * cr, y - 0.344136 * cb - 0.714136 * cr, y + 1.772 * cb);
    COLOR = vec4(clamp(rgb, 0.0, 1.0), 1.0);
}";

    private readonly IPEndPoint host;
    private readonly uint? guestId;
    private readonly object decoderLock = new object();

    private StreamGuestSession session;
    private StreamGuestPads pads;
    private StreamGuestAudio audio;
    private StreamDecoder decoder;
    private TextureRect video;
    private ShaderMaterial material;
    private Label status;

    private readonly Image lumaImage = new Image();
    private readonly Image chromaBlueImage = new Image();
    private readonly Image chromaRedImage = new Image();
    private ImageTexture lumaTexture;
    private ImageTexture chromaBlueTexture;
    private ImageTexture chromaRedTexture;

    private readonly StreamPlayout playout = new StreamPlayout();
    private DecodedFrame shownFrame;
    private long framesShown;
    private long lastStatusMs;
    private long framesShownAtLastStatus;
    private double showDelayTotalMs;
    private int showDelaySamples;
    private string disconnectReason;

    // Measurements for the log, gathered over each reporting period. The ones touched by the
    // network thread are only ever added to or replaced whole.
    private const double ReportEverySeconds = 5.0;
    private double secondsSinceReport;
    private int loopsThisReport;
    private double longestLoopMs;
    private long framesShownAtLastReport;
    private long framesArrived;
    private long framesDecoded;
    private long decodeTicksTotal;
    private long longestDecodeTicks;
    private long longestArrivalGapTicks;
    private long lastArrivalTicks;

    // What the view changed about the app while open, to put back when it closes.
    private CanvasItem hiddenScene;
    private Control focusedBeforeOpening;
    private DisplayServer.VSyncMode vsyncBeforeOpening;
    private int maxFpsBeforeOpening;
    private bool lowProcessorModeBeforeOpening;

    // Input is read and the picture is put on screen once per loop. Running the loop faster than
    // the screen refreshes, without waiting for the screen, shortens both waits.
    private const int LoopsPerSecondWhileOpen = 240;
    private readonly string appDirectory = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());

    // True while a stream fills the screen. The controller then belongs to the host's game,
    // so the rest of RomBox ignores it.
    public static bool IsOpen => openViews > 0;
    private static int openViews;

    // For tests: send a made-up controller in place of the real ones.
    public bool SendsTestInput { get; set; }

    // For tests beside a real session: send no controllers at all, so none are plugged in on the host.
    public bool SendsNoInput { get; set; }

    public StreamGuestView(IPEndPoint host, uint? guestId = null)
    {
        this.host = host;
        this.guestId = guestId;
        Layer = 100;
    }

    public override void _EnterTree()
    {
        openViews++;

        // The library behind the view would be drawn every frame for nobody to see. On a
        // high-resolution laptop screen that alone can halve the frame rate.
        focusedBeforeOpening = GetViewport().GuiGetFocusOwner();

        if (GetTree().CurrentScene is CanvasItem scene && scene.Visible)
        {
            hiddenScene = scene;
            scene.Visible = false;
        }

        vsyncBeforeOpening = DisplayServer.WindowGetVsyncMode();
        maxFpsBeforeOpening = Engine.MaxFps;
        lowProcessorModeBeforeOpening = OS.LowProcessorUsageMode;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = LoopsPerSecondWhileOpen;

        OS.LowProcessorUsageMode = false;
    }

    public override void _Ready()
    {
        var background = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(background);

        material = new ShaderMaterial { Shader = new Shader { Code = ShaderCode } };
        video = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Material = material
        };
        video.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(video);

        status = new Label { Position = new Vector2(16, 12), Text = $"Connecting to {host}..." };
        status.AddThemeColorOverride("font_color", Colors.White);
        status.AddThemeColorOverride("font_outline_color", Colors.Black);
        status.AddThemeConstantOverride("outline_size", 6);
        AddChild(status);

        audio = new StreamGuestAudio(this);
        session = new StreamGuestSession(host, Log, guestId);
        session.AudioReceived += audio.Receive;
        session.FormatChanged += OnFormatChanged;
        session.FrameReceived += OnFrameReceived;
        session.Disconnected += reason => disconnectReason = reason;
        pads = new StreamGuestPads(session) { SendsTestPattern = SendsTestInput };
        session.Start();
    }

    private static void Log(string line)
    {
        GD.Print(line);
    }

    // Runs on the network thread.
    private void OnFormatChanged(StreamVideoFormat format)
    {
        lock (decoderLock)
        {
            decoder?.Dispose();
            playout.Reset();
            decoder = new StreamDecoder(format, Log);
            decoder.FrameDecoded += OnFrameDecoded;

            if (!decoder.Start(appDirectory))
            {
                disconnectReason = "RomBox could not start the video decoder. " + (Libav.LoadError ?? "");
                decoder.Dispose();
                decoder = null;
            }
        }
    }

    // Runs on the network thread.
    private void OnFrameReceived(byte[] frame, bool isKeyframe, uint hostTimeMs)
    {
        long arrivedAt = Stopwatch.GetTimestamp();

        if (lastArrivalTicks != 0 && arrivedAt - lastArrivalTicks > longestArrivalGapTicks)
        {
            longestArrivalGapTicks = arrivedAt - lastArrivalTicks;
        }

        lastArrivalTicks = arrivedAt;
        Interlocked.Increment(ref framesArrived);

        lock (decoderLock)
        {
            decoder?.Decode(frame, isKeyframe, hostTimeMs);
        }

        long decodeTicks = Stopwatch.GetTimestamp() - arrivedAt;
        Interlocked.Add(ref decodeTicksTotal, decodeTicks);

        if (decodeTicks > longestDecodeTicks)
        {
            longestDecodeTicks = decodeTicks;
        }
    }

    // Runs on the network thread. The playout decides when the frame is shown.
    private void OnFrameDecoded(DecodedFrame frame)
    {
        Interlocked.Increment(ref framesDecoded);
        WriteTestCounter(frame);
        playout.Add(frame, NowMs(), Recycle);
    }

    private void Recycle(DecodedFrame frame)
    {
        decoder?.Recycle(frame);
    }

    private static double NowMs()
    {
        return Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    }

    // For delay measurements: when ROMBOX_TEST_CLOCK_FILE names a file, the 16-block frame
    // counter of the test clock is read from each decoded picture and written there, so a script
    // can compare it with the counter the clock window is showing at that moment.
    private static readonly string TestClockFile = System.Environment.GetEnvironmentVariable("ROMBOX_TEST_CLOCK_FILE");

    private static void WriteTestCounter(DecodedFrame frame)
    {
        if (string.IsNullOrEmpty(TestClockFile))
        {
            return;
        }

        int row = frame.Height / 2 * frame.Width;
        int counter = 0;

        for (int bit = 0; bit < 16; bit++)
        {
            int x = (25 + 50 * bit) * frame.Width / 800;

            if (frame.Luma[row + x] > 128)
            {
                counter |= 1 << bit;
            }
        }

        try
        {
            System.IO.File.WriteAllText(TestClockFile, counter.ToString());
        }
        catch (System.IO.IOException)
        {
        }
    }

    public override void _Process(double delta)
    {
        DecodedFrame frame = playout.TakeDue(NowMs(), Recycle);

        if (frame != null)
        {
            Upload(frame);
            decoder?.Recycle(shownFrame);
            shownFrame = frame;
            framesShown++;
            showDelayTotalMs += (Stopwatch.GetTimestamp() - frame.DecodedAtTicks) * 1000.0 / Stopwatch.Frequency;
            showDelaySamples++;
        }

        if (!SendsNoInput)
        {
            pads.Update(GetWindow().HasFocus());
        }

        audio.Update();
        UpdateStatus();
        Report(delta);
    }

    // Every few seconds the log says where frames went and how long each stage took, so a
    // stutter or a delay can be traced to the network, the decoder or this app's own loop.
    private void Report(double delta)
    {
        loopsThisReport++;
        longestLoopMs = Math.Max(longestLoopMs, delta * 1000.0);
        secondsSinceReport += delta;

        if (secondsSinceReport < ReportEverySeconds)
        {
            return;
        }

        long arrived = Interlocked.Exchange(ref framesArrived, 0);
        long decoded = Interlocked.Exchange(ref framesDecoded, 0);
        double decodeTotalMs = Interlocked.Exchange(ref decodeTicksTotal, 0) * 1000.0 / Stopwatch.Frequency;
        double longestDecodeMs = Interlocked.Exchange(ref longestDecodeTicks, 0) * 1000.0 / Stopwatch.Frequency;
        double longestGapMs = Interlocked.Exchange(ref longestArrivalGapTicks, 0) * 1000.0 / Stopwatch.Frequency;
        long shown = framesShown - framesShownAtLastReport;

        GD.Print($"[Stream] guest: {arrived / secondsSinceReport:0.0} fps arrived, {decoded / secondsSinceReport:0.0} decoded, {shown / secondsSinceReport:0.0} shown; "
            + $"longest wait between frames {longestGapMs:0} ms; decode {(arrived > 0 ? decodeTotalMs / arrived : 0):0.0} ms average, {longestDecodeMs:0.0} longest; "
            + $"held {playout.WaitMs:0} ms to even out arrival, {playout.FramesSkipped} skipped; "
            + $"loop {loopsThisReport / secondsSinceReport:0} per second, {longestLoopMs:0} ms longest; ping {session.RoundTripMs:0} ms; lost {session.FramesLost}; "
            + $"controller states sent {pads.StatesSent}.");
        GD.Print($"[Stream] sound: played {audio.FramesPlayed} frames, peak {audio.PeakLevel:0.000}, lost {audio.PacketsLost}, skipped {audio.FramesSkipped}, ran dry {audio.TimesRunDry} times, {audio.BufferedMs} ms waiting, keeping {audio.LevelMs} ms in hand.");

        framesShownAtLastReport = framesShown;
        loopsThisReport = 0;
        longestLoopMs = 0;
        secondsSinceReport = 0;
    }

    private void Upload(DecodedFrame frame)
    {
        int chromaWidth = frame.Width / 2;
        int chromaHeight = frame.Height / 2;

        lumaImage.SetData(frame.Width, frame.Height, false, Image.Format.R8, frame.Luma);
        chromaBlueImage.SetData(chromaWidth, chromaHeight, false, Image.Format.R8, frame.ChromaBlue);
        chromaRedImage.SetData(chromaWidth, chromaHeight, false, Image.Format.R8, frame.ChromaRed);

        if (lumaTexture == null || lumaTexture.GetWidth() != frame.Width || lumaTexture.GetHeight() != frame.Height)
        {
            lumaTexture = ImageTexture.CreateFromImage(lumaImage);
            chromaBlueTexture = ImageTexture.CreateFromImage(chromaBlueImage);
            chromaRedTexture = ImageTexture.CreateFromImage(chromaRedImage);
            video.Texture = lumaTexture;
            material.SetShaderParameter("chroma_blue", chromaBlueTexture);
            material.SetShaderParameter("chroma_red", chromaRedTexture);
        }
        else
        {
            lumaTexture.Update(lumaImage);
            chromaBlueTexture.Update(chromaBlueImage);
            chromaRedTexture.Update(chromaRedImage);
        }

        material.SetShaderParameter("full_range", frame.IsFullRange);
    }

    private void UpdateStatus()
    {
        long now = (long)Time.GetTicksMsec();

        if (now - lastStatusMs < 1000)
        {
            return;
        }

        double seconds = (now - lastStatusMs) / 1000.0;
        long fps = (long)Math.Round((framesShown - framesShownAtLastStatus) / seconds);
        double showDelay = showDelaySamples > 0 ? showDelayTotalMs / showDelaySamples : 0;

        status.Text = disconnectReason
            ?? (session.IsConnected
                ? $"{fps} fps   ping {session.RoundTripMs:0} ms   network {session.LastNetworkDelayMs:0} ms   decode to screen {showDelay:0} ms   lost {session.FramesLost}   smoothing {playout.WaitMs:0} ms   sound {audio.BufferedMs} ms waiting"
                : $"Connecting to {host}...");

        lastStatusMs = now;
        framesShownAtLastStatus = framesShown;
        showDelayTotalMs = 0;
        showDelaySamples = 0;
    }

    // Escape on the keyboard closes the view. Controller buttons all go to the host's game.
    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }

    public override void _ExitTree()
    {
        openViews--;
        DisplayServer.WindowSetVsyncMode(vsyncBeforeOpening);
        Engine.MaxFps = maxFpsBeforeOpening;
        OS.LowProcessorUsageMode = lowProcessorModeBeforeOpening;

        if (hiddenScene != null && IsInstanceValid(hiddenScene))
        {
            hiddenScene.Visible = true;

            // Hiding the scene took the focus away from whatever had it.
            if (focusedBeforeOpening != null && IsInstanceValid(focusedBeforeOpening) && focusedBeforeOpening.IsVisibleInTree())
            {
                focusedBeforeOpening.CallDeferred(Control.MethodName.GrabFocus);
            }
        }

        hiddenScene = null;
        session?.Dispose();
        session = null;

        lock (decoderLock)
        {
            decoder?.Dispose();
            decoder = null;
        }
    }
}
