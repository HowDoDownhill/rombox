using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

// Finds the ffmpeg program used for capture, encoding and decoding, and works out which
// hardware encoder this computer can use.
public static class FfmpegTool
{
    public const string OverrideVariable = "ROMBOX_FFMPEG";

    private static string cachedPath;
    private static string cachedEncoder;

    // ROMBOX_FFMPEG, then tools/ffmpeg/<os>/ beside the app, then the system PATH.
    public static string FindExecutable(string appDirectory = null)
    {
        if (cachedPath != null)
        {
            return cachedPath;
        }

        string fileName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string overridePath = Environment.GetEnvironmentVariable(OverrideVariable);

        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            return cachedPath = overridePath;
        }

        string baseDirectory = appDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "";
        string platformFolder = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        string bundledPath = Path.Combine(baseDirectory, "tools", "ffmpeg", platformFolder, fileName);

        if (File.Exists(bundledPath))
        {
            return cachedPath = bundledPath;
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            string candidate = Path.Combine(directory.Trim('"'), fileName);

            if (File.Exists(candidate))
            {
                return cachedPath = candidate;
            }
        }

        return null;
    }

    // The first encoder in this platform's preference order that encodes a test frame.
    public static string PickEncoder(string ffmpegPath, Action<string> log)
    {
        if (cachedEncoder != null)
        {
            return cachedEncoder;
        }

        string[] preference = OperatingSystem.IsWindows()
            ? new[] { "h264_nvenc", "h264_amf", "h264_qsv", "h264_mf" }
            : new[] { "h264_nvenc", "h264_vaapi", "libx264", "libopenh264" };

        string listed = Run(ffmpegPath, "-hide_banner -encoders", 10000).Output;

        foreach (string encoder in preference.Where(name => listed.Contains(" " + name + " ")))
        {
            string probeArguments = $"-hide_banner -loglevel error -f lavfi -i color=c=black:s=256x256:r=30 -frames:v 3 {StreamEncoderArguments.ProbeArguments(encoder)} -f null -";
            ProcessResult probe = Run(ffmpegPath, probeArguments, 15000);

            if (probe.ExitCode == 0)
            {
                log?.Invoke($"[Stream] using the {encoder} encoder.");
                return cachedEncoder = encoder;
            }

            log?.Invoke($"[Stream] {encoder} is listed but did not work: {FirstLine(probe.Output)}");
        }

        return null;
    }

    public struct ProcessResult
    {
        public int ExitCode;
        public string Output;
    }

    public static ProcessResult Run(string path, string arguments, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo(path, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo);
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(timeoutMs))
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            return new ProcessResult { ExitCode = -1, Output = "timed out" };
        }

        return new ProcessResult { ExitCode = process.ExitCode, Output = outputTask.Result + errorTask.Result };
    }

    private static string FirstLine(string text)
    {
        return (text ?? "").Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? "";
    }
}

// The ffmpeg settings for each encoder, tuned for the lowest delay: no B-frames, no look-ahead,
// a small rate buffer, and a keyframe every second so a guest recovers quickly from loss.
public static class StreamEncoderArguments
{
    public static string ProbeArguments(string encoder)
    {
        return encoder switch
        {
            "h264_vaapi" => "-vaapi_device /dev/dri/renderD128 -vf format=nv12,hwupload -c:v h264_vaapi",
            "h264_qsv" => "-vf format=nv12 -c:v h264_qsv",
            "h264_mf" => "-vf format=nv12 -c:v h264_mf -hw_encoding 1",
            _ => $"-c:v {encoder}"
        };
    }

    // Filters that turn the capture output into what the encoder accepts. The Windows window
    // capture produces GPU frames, which NVENC and AMF take as they are.
    public static string VideoFilter(string encoder, bool capturesGpuFrames)
    {
        var steps = new List<string>();

        switch (encoder)
        {
            case "h264_nvenc":
            case "h264_amf":
                break;
            case "h264_qsv":
                steps.Add(capturesGpuFrames ? "hwmap=derive_device=qsv,format=qsv" : "format=nv12");
                break;
            case "h264_vaapi":
                if (capturesGpuFrames) steps.Add("hwdownload,format=bgra");
                steps.Add("format=nv12,hwupload");
                break;
            default:
                if (capturesGpuFrames) steps.Add("hwdownload,format=bgra");
                steps.Add("format=nv12");
                break;
        }

        return string.Join(",", steps);
    }

    public static string EncoderOptions(string encoder, int bitrateKbps, int frameRate)
    {
        int bufferKbits = Math.Max(bitrateKbps * 2 / Math.Max(frameRate, 1), 100);
        string rate = $"-b:v {bitrateKbps}k -maxrate {bitrateKbps}k -bufsize {bufferKbits}k";
        int gop = frameRate;

        return encoder switch
        {
            "h264_nvenc" => $"-c:v h264_nvenc -preset p1 -tune ull -zerolatency 1 -delay 0 -rc-lookahead 0 -bf 0 -rc cbr {rate} -g {gop} -forced-idr 1",
            "h264_amf" => $"-c:v h264_amf -usage ultralowlatency -quality speed -rc cbr {rate} -bf 0 -g {gop}",
            "h264_qsv" => $"-c:v h264_qsv -preset veryfast -low_delay_brc 1 -async_depth 1 -bf 0 {rate} -g {gop}",
            "h264_mf" => $"-c:v h264_mf -hw_encoding 1 -scenario display_remoting -rate_control cbr {rate} -bf 0 -g {gop}",
            "h264_vaapi" => $"-c:v h264_vaapi -rc_mode CBR {rate} -bf 0 -g {gop}",
            "libx264" => $"-c:v libx264 -preset ultrafast -tune zerolatency {rate} -bf 0 -g {gop}",
            _ => $"-c:v {encoder} {rate} -g {gop}"
        };
    }

    public static string DeviceOptions(string encoder)
    {
        return encoder == "h264_vaapi" ? "-vaapi_device /dev/dri/renderD128" : "";
    }
}
