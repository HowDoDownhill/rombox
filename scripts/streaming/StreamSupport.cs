using System;

// Whether this computer can host or join a session, checked before a lobby opens so the player
// is told at once. Without the check a host found out only when the game started and no guest
// received a picture.
public static class StreamSupport
{
    // Null when this computer can host; otherwise a sentence for the player saying why it cannot.
    public static string WhyCannotHost(string appDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return "Hosting a session works on Windows and Linux.";
        }

        if (FfmpegTool.FindExecutable(appDirectory) == null)
        {
            return OperatingSystem.IsWindows()
                ? "RomBox's FFmpeg files are missing, so it cannot send the game to guests. Reinstalling RomBox puts them back."
                : "FFmpeg is not installed, so RomBox cannot send the game to guests. Install your system's ffmpeg package, then host again.";
        }

        if (OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return "Hosting on Linux needs X11 or XWayland, and this desktop has neither running.";
        }

        return null;
    }

    // Null when this computer can show a host's game; otherwise a sentence saying why it cannot.
    public static string WhyCannotJoin(string appDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
        {
            return "Joining a session with RomBox works on Windows and Linux. On this device, ask the host for a browser link.";
        }

        if (!Libav.TryLoad(appDirectory, null))
        {
            return OperatingSystem.IsWindows()
                ? "RomBox's FFmpeg files are missing, so it cannot show the host's game. Reinstalling RomBox puts them back."
                : "FFmpeg is not installed, so RomBox cannot show the host's game. Install your system's ffmpeg package, then start RomBox again.";
        }

        return null;
    }
}
