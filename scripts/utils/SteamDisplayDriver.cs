using Godot;
using System;

public static class SteamDisplayDriver
{
    public const string X11DriverName = "x11";
    public const string RelaunchAttemptedVariable = "ROMM_DISPLAY_RELAUNCH_ATTEMPTED";

    public static bool ShouldRelaunchOnX11()
    {
        return OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId"))
            && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(RelaunchAttemptedVariable))
            && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("DISPLAY"))
            && DisplayServer.GetName().Equals("Wayland", StringComparison.OrdinalIgnoreCase);
    }
}
