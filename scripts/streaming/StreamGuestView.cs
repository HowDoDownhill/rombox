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

    private DecodedFrame pendingFrame;
    private DecodedFrame shownFrame;
    private long framesShown;
    private long lastStatusMs;
    private int statusUpdates;
    private long framesShownAtLastStatus;
    private double showDelayTotalMs;
    private int showDelaySamples;
    private string disconnectReason;
    private readonly string appDirectory = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());

    // True while a stream fills the screen. The controller then belongs to the host's game,
    // so the rest of RomBox ignores it.
    public static bool IsOpen => openViews > 0;
    private static int openViews;

    // For tests: send a made-up controller in place of the real ones.
    public bool SendsTestInput { get; set; }

    public StreamGuestView(IPEndPoint host, uint? guestId = null)
    {
        this.host = host;
        this.guestId = guestId;
        Layer = 100;
    }

    public override void _EnterTree()
    {
        openViews++;
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
        lock (decoderLock)
        {
            decoder?.Decode(frame, isKeyframe, hostTimeMs);
        }
    }

    // Runs on the network thread. Only the newest frame is kept; a frame the screen never got to
    // show goes back to the decoder.
    private void OnFrameDecoded(DecodedFrame frame)
    {
        DecodedFrame replaced = Interlocked.Exchange(ref pendingFrame, frame);
        decoder?.Recycle(replaced);
    }

    public override void _Process(double delta)
    {
        DecodedFrame frame = Interlocked.Exchange(ref pendingFrame, null);

        if (frame != null)
        {
            Upload(frame);
            decoder?.Recycle(shownFrame);
            shownFrame = frame;
            framesShown++;
            showDelayTotalMs += (Stopwatch.GetTimestamp() - frame.DecodedAtTicks) * 1000.0 / Stopwatch.Frequency;
            showDelaySamples++;
        }

        pads.Update(GetWindow().HasFocus());
        audio.Update();
        UpdateStatus();
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
                ? $"{fps} fps   ping {session.RoundTripMs:0} ms   network {session.LastNetworkDelayMs:0} ms   decode to screen {showDelay:0} ms   lost {session.FramesLost}   sound {audio.BufferedMs} ms waiting"
                : $"Connecting to {host}...");

        // Every fifth status line also goes to the log, so a test on another machine can be read over SSH.
        if (++statusUpdates % 5 == 0)
        {
            GD.Print($"[Stream] {status.Text}");
            GD.Print($"[Stream] sound: played {audio.FramesPlayed} frames, peak {audio.PeakLevel:0.000}, lost {audio.PacketsLost}, skipped {audio.FramesSkipped}, ran dry {audio.TimesRunDry} times.");
        }

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
        session?.Dispose();
        session = null;

        lock (decoderLock)
        {
            decoder?.Dispose();
            decoder = null;
        }
    }
}
