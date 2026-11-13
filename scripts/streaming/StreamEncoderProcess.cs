using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

// What the host captures. On Windows a window is found by handle, program name or title. On
// Linux an X11 window id is captured (emulators running under XWayland count); a value of 0
// captures the whole X display.
public sealed class StreamCaptureTarget
{
    public long WindowHandle;
    public string WindowExe;
    public string WindowTitle;
    public long X11WindowId;
    // Linux: the program whose sound is captured. 0 captures the whole desktop's sound.
    public int ProcessId;
    // Linux: the window's size when it was found, or 0 when unknown.
    public int X11Width;
    public int X11Height;

    public static StreamCaptureTarget Parse(string text)
    {
        var target = new StreamCaptureTarget();
        int colon = text.IndexOf(':');
        string kind = colon > 0 ? text.Substring(0, colon) : "title";
        string value = colon > 0 ? text.Substring(colon + 1) : text;

        switch (kind)
        {
            case "hwnd": target.WindowHandle = long.Parse(value, CultureInfo.InvariantCulture); break;
            case "exe": target.WindowExe = value; break;
            case "x11": target.X11WindowId = Convert.ToInt64(value, value.StartsWith("0x") ? 16 : 10); break;
            // Linux: a running program, by process id. Its window and its sound are captured.
            case "pid":
                target.ProcessId = int.Parse(value, CultureInfo.InvariantCulture);
                X11Windows.Found? window = X11Windows.FindForProcess(target.ProcessId);
                target.X11WindowId = window?.WindowId ?? 0;
                target.X11Width = window?.Width ?? 0;
                target.X11Height = window?.Height ?? 0;

                if (target.X11WindowId == 0)
                {
                    Godot.GD.Print($"[Stream] no window found: {X11Windows.LastProblem}.");
                }

                break;
            default: target.WindowTitle = value; break;
        }

        return target;
    }

    public override string ToString()
    {
        if (WindowHandle != 0) return $"window {WindowHandle}";
        if (!string.IsNullOrEmpty(WindowExe)) return $"program {WindowExe}";
        if (!string.IsNullOrEmpty(WindowTitle)) return $"window titled {WindowTitle}";
        return X11WindowId != 0 ? $"X11 window 0x{X11WindowId:X}" : "the X display";
    }
}

public sealed class StreamEncoderSettings
{
    public int FrameRate = 60;
    public int BitrateKbps = 10000;
    // The stream is scaled down to fit inside this size. H.264 hardware encoders stop at 4096
    // pixels wide, so 3840x2160 is the largest size offered.
    public int MaximumWidth = 1920;
    public int MaximumHeight = 1080;
    // A window wider than this shape has its sides cut off evenly. A console game on an ultrawide
    // monitor sits in the middle with empty space either side, and sending that space would
    // leave the guest with bars on all four sides. 0 keeps the window's own shape.
    public double WidestAspect = 16.0 / 9.0;
    public string Encoder;

    // Pictures taller than this are cut into strips so guests can decode them on several threads.
    public const int TallestSingleStripHeight = 1440;
    public const int StripsForTallPictures = 4;
}

// Runs ffmpeg to capture and encode, and reports each encoded frame as soon as ffmpeg writes it.
public sealed class StreamEncoderProcess : IDisposable
{
    private readonly string ffmpegPath;
    private readonly StreamCaptureTarget target;
    private readonly StreamEncoderSettings settings;
    private readonly Action<string> log;

    private Process process;
    private Thread readerThread;
    private Thread errorThread;
    private volatile bool stopping;

    private byte[] decoderSetup;
    private int width;
    private int height;

    public string Encoder { get; private set; }

    // Raised once the size and decoder setup are known, before the first frame.
    public event Action<StreamVideoFormat> FormatReady;
    public event Action<byte[], bool> FrameEncoded;
    public event Action<int> Exited;

    public StreamEncoderProcess(string ffmpegPath, StreamCaptureTarget target, StreamEncoderSettings settings, Action<string> log)
    {
        this.ffmpegPath = ffmpegPath;
        this.target = target;
        this.settings = settings;
        this.log = log;
    }

    public bool Start()
    {
        Encoder = settings.Encoder ?? FfmpegTool.PickEncoder(ffmpegPath, log);

        if (Encoder == null)
        {
            log?.Invoke("[Stream] no working H.264 encoder was found.");
            return false;
        }

        string arguments = BuildArguments();
        log?.Invoke($"[Stream] capturing {target} with {Encoder}.");
        log?.Invoke($"[Stream] ffmpeg {arguments}");

        var startInfo = new ProcessStartInfo(ffmpegPath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process = Process.Start(startInfo);

        if (process == null)
        {
            return false;
        }

        readerThread = new Thread(ReadOutput) { IsBackground = true, Name = "Stream encoder output" };
        errorThread = new Thread(ReadErrors) { IsBackground = true, Name = "Stream encoder log" };
        readerThread.Start();
        errorThread.Start();
        return true;
    }

    private string BuildArguments()
    {
        bool capturesGpuFrames = OperatingSystem.IsWindows();
        string input;
        string limiter = "";
        int declaredFrameRate = settings.FrameRate;

        if (OperatingSystem.IsWindows())
        {
            string source = target.WindowHandle != 0 ? $"hwnd={target.WindowHandle}"
                : !string.IsNullOrEmpty(target.WindowExe) ? $"window_exe={EscapeFilterValue(target.WindowExe)}"
                : $"window_title={EscapeFilterValue(target.WindowTitle ?? "")}";
            // Even sizes keep every encoder and the 4:2:0 colour layout happy.
            // The capture's own cap drops any frame that follows the last one too closely. A game
            // at 60 fps shows its frames at slightly uneven gaps, so a cap of 60 lost about one
            // frame in six. The cap is set high and the limiter below keeps the rate instead.
            declaredFrameRate = settings.FrameRate * 4;
            input = $"-f lavfi -i \"gfxcapture={source}:max_framerate={declaredFrameRate}:capture_cursor=0:{WindowsOutputSize()}\"";
            limiter = RateLimiter(settings.FrameRate);
        }
        else
        {
            string display = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";
            string window = target.X11WindowId != 0 ? $"-window_id 0x{target.X11WindowId:X} " : "";
            input = $"-f x11grab -framerate {settings.FrameRate} -draw_mouse 0 {window}-i {display}";
            limiter = LinuxScaling();
        }

        string encoderFilter = StreamEncoderArguments.VideoFilter(Encoder, capturesGpuFrames);
        string filter = string.Join(",", new[] { limiter, encoderFilter }.Where(step => !string.IsNullOrEmpty(step)));
        string filterArgument = string.IsNullOrEmpty(filter) ? "" : $"-vf \"{filter}\" ";

        return $"-hide_banner -loglevel warning -nostats {StreamEncoderArguments.DeviceOptions(Encoder)} {input} {filterArgument}-fps_mode passthrough "
            + $"{StreamEncoderArguments.EncoderOptions(Encoder, settings.BitrateKbps, settings.FrameRate, declaredFrameRate, settings.MaximumHeight > StreamEncoderSettings.TallestSingleStripHeight ? StreamEncoderSettings.StripsForTallPictures : 1)} -an -f flv -flush_packets 1 pipe:1";
    }

    // Linux: the picture is shrunk to fit the size limit, never enlarged, with even sides.
    //
    // Shrinking on the processor is slow: a game filling a 3840x2400 screen came out at 41
    // frames a second and the host's own sound stuttered. With an NVIDIA encoder and a window
    // of known size, the frame is handed to the graphics card as it is and shrunk there.
    private string LinuxScaling()
    {
        int width = target.X11Width;
        int height = target.X11Height;

        if (width <= 0 || height <= 0)
        {
            return $"scale=w='min(iw,{settings.MaximumWidth})':h='min(ih,{settings.MaximumHeight})':force_original_aspect_ratio=decrease:force_divisible_by=2";
        }

        double shrink = Math.Min(1.0, Math.Min(settings.MaximumWidth / (double)width, settings.MaximumHeight / (double)height));
        int outputWidth = Math.Max(2, (int)Math.Round(width * shrink) & ~1);
        int outputHeight = Math.Max(2, (int)Math.Round(height * shrink) & ~1);

        if (outputWidth == width && outputHeight == height)
        {
            return "";
        }

        return Encoder == "h264_nvenc"
            ? $"hwupload_cuda,scale_cuda=w={outputWidth}:h={outputHeight}:interp_algo=bilinear"
            : $"scale=w={outputWidth}:h={outputHeight}:flags=fast_bilinear";
    }

    // A window wider than the widest shape loses its sides, and one larger than the limit is
    // scaled down to fit. Even sizes keep every encoder and the 4:2:0 colour layout happy.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private string WindowsOutputSize()
    {
        (int windowWidth, int windowHeight) = WindowsProcessAudioCapture.FindWindowSize(target);
        int width = windowWidth;
        int height = windowHeight;
        string crop = "";

        if (settings.WidestAspect > 0 && height > 0 && width > height * settings.WidestAspect + 2)
        {
            int cutEachSide = (int)((width - height * settings.WidestAspect) / 2);
            width -= cutEachSide * 2;
            crop = $"crop_left={cutEachSide}:crop_right={cutEachSide}:";
        }

        if (width <= settings.MaximumWidth && height <= settings.MaximumHeight)
        {
            if (crop.Length > 0)
            {
                log?.Invoke($"[Stream] the window is {windowWidth}x{windowHeight}; streaming the middle {width}x{height}.");
            }

            return crop + "width=-2:height=-2";
        }

        double scale = Math.Min(settings.MaximumWidth / (double)width, settings.MaximumHeight / (double)height);
        int scaledWidth = (int)Math.Round(width * scale) & ~1;
        int scaledHeight = (int)Math.Round(height * scale) & ~1;
        log?.Invoke($"[Stream] the window is {windowWidth}x{windowHeight}; streaming {(crop.Length > 0 ? $"the middle {width}x{height} " : "it ")}at {scaledWidth}x{scaledHeight}.");
        return $"{crop}width={scaledWidth}:height={scaledHeight}:resize_mode=scale:scale_mode=bicubic";
    }

    // Lets frames through at their own timing, up to a little over the target rate on average.
    // Each second adds that many passes to a store that holds at most two; a frame goes through
    // when a pass is available. A steady 60 fps game keeps every frame and adds no delay; a
    // window drawing 165 times a second is thinned to the target. The margin of two frames a
    // second covers consoles that run slightly above 60.
    private static string RateLimiter(int frameRate)
    {
        // Commas inside the expression are escaped, because a bare comma separates filters.
        const string comma = "\\,";
        return $"select='st(0,min(ld(0)+(t-ld(1))*{frameRate + 2}{comma}2));st(1{comma}t);if(gte(ld(0){comma}1){comma}st(0{comma}ld(0)-1)*0+1{comma}0)'";
    }

    // gfxcapture takes regular expressions. Colons and backslashes are filter syntax.
    private static string EscapeFilterValue(string value)
    {
        return value.Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
    }

    private void ReadOutput()
    {
        var reader = new FlvVideoReader(process.StandardOutput.BaseStream);
        bool formatSent = false;

        reader.MetadataRead += () =>
        {
            width = reader.Width;
            height = reader.Height;
        };

        reader.DecoderSetupRead += setup => decoderSetup = setup;

        reader.FrameRead += (frame, isKeyframe) =>
        {
            if (!formatSent)
            {
                if (decoderSetup == null || width <= 0 || height <= 0)
                {
                    return;
                }

                FormatReady?.Invoke(new StreamVideoFormat
                {
                    Width = width,
                    Height = height,
                    FrameRate = settings.FrameRate,
                    DecoderSetup = decoderSetup
                });
                formatSent = true;
            }

            FrameEncoded?.Invoke(frame, isKeyframe);
        };

        if (!reader.ReadAll())
        {
            log?.Invoke("[Stream] ffmpeg did not produce an FLV stream.");
        }

        process.WaitForExit();
        int exitCode = process.ExitCode;

        if (!stopping)
        {
            log?.Invoke($"[Stream] capture stopped (ffmpeg exit code {exitCode}).");
        }

        Exited?.Invoke(exitCode);
    }

    private void ReadErrors()
    {
        string line;

        while ((line = process.StandardError.ReadLine()) != null)
        {
            if (line.Length > 0)
            {
                log?.Invoke($"[Stream] ffmpeg: {line}");
            }
        }
    }

    public void Dispose()
    {
        stopping = true;

        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                // "q" asks ffmpeg to finish cleanly; the kill covers a capture that is stuck.
                process.StandardInput.Write('q');
                process.StandardInput.Flush();

                if (!process.WaitForExit(1500))
                {
                    process.Kill(true);
                }
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException || exception is System.IO.IOException)
        {
        }

        process.Dispose();
        process = null;
    }
}
