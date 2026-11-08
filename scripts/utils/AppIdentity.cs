using Godot;
using System;
using System.IO;

// The app's name in every form it takes on disk and on screen, plus the name it shipped under
// up to 1.2.2. Installs from that time keep the old file names until LegacyNameMigration moves them.
public static class AppIdentity
{
    public const string DisplayName = "RomBox";
    public const string FileBaseName = "rombox";

    public const string LegacyDisplayName = "RomM Frontend";
    public const string LegacyFileBaseName = "romm-frontend";

    public static string ReleasePlatformName =>
        OS.HasFeature("windows") ? "windows"
        : OS.HasFeature("macos") ? "macos"
        : "linux";

    public static string ReleaseZipName => $"{FileBaseName}-{ReleasePlatformName}.zip";
    public static string LegacyReleaseZipName => $"{LegacyFileBaseName}-{ReleasePlatformName}.zip";

    // Godot loads the .pck that shares the executable's base name, so the two always move together.
    public static string ExecutableExtension => OS.HasFeature("windows") ? ".exe" : ".x86_64";

    public static bool IsRunningUnderLegacyName()
    {
        string executableName = Path.GetFileNameWithoutExtension(OS.GetExecutablePath());
        return string.Equals(executableName, LegacyFileBaseName, StringComparison.OrdinalIgnoreCase);
    }

    public static string ExecutablePathInDirectory(string directory) => Path.Combine(directory, FileBaseName + ExecutableExtension);
}
