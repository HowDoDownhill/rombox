using Godot;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

// Moves a Windows or Linux install from the romm-frontend file names to the rombox ones.
//
// Installs up to 1.2.2 update by extracting the release zip over the app folder and restarting
// romm-frontend.exe. The release ships a bridge zip under the old asset name whose executable and
// .pck carry the old names, so that restart runs current code. That copy places rombox.exe and
// rombox.pck beside itself, starts them and quits. The rombox copy then deletes the old files,
// unless a Steam shortcut still points at the old executable.
public static class LegacyNameMigration
{
    private static readonly string[] LegacyFileNames =
    {
        AppIdentity.LegacyFileBaseName + ".exe",
        AppIdentity.LegacyFileBaseName + ".console.exe",
        AppIdentity.LegacyFileBaseName + ".pck",
        AppIdentity.LegacyFileBaseName + ".x86_64",
        AppIdentity.LegacyFileBaseName + ".sh",
    };

    private static bool AppliesToThisBuild => !OS.HasFeature("editor") && (OS.HasFeature("windows") || OS.HasFeature("linux")) && !OS.HasFeature("android");

    // Returns true when a copy under the new name was started and this process should quit.
    public static bool RelaunchUnderNewNameIfNeeded()
    {
        if (!AppliesToThisBuild || !AppIdentity.IsRunningUnderLegacyName())
        {
            return false;
        }

        try
        {
            string legacyExecutablePath = OS.GetExecutablePath();
            string appDirectory = Path.GetDirectoryName(legacyExecutablePath);
            string newExecutablePath = AppIdentity.ExecutablePathInDirectory(appDirectory);
            string legacyPackPath = Path.Combine(appDirectory, AppIdentity.LegacyFileBaseName + ".pck");
            string newPackPath = Path.Combine(appDirectory, AppIdentity.FileBaseName + ".pck");

            if (!File.Exists(newPackPath))
            {
                if (!File.Exists(legacyPackPath))
                {
                    GD.PrintErr($"[Rename] Neither {newPackPath} nor {legacyPackPath} exists, so the app stays under its old name.");
                    return false;
                }

                File.Copy(legacyPackPath, newPackPath);
            }

            if (!File.Exists(newExecutablePath))
            {
                File.Copy(legacyExecutablePath, newExecutablePath);

                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(newExecutablePath, File.GetUnixFileMode(legacyExecutablePath) | UnixFileMode.UserExecute);
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = newExecutablePath,
                UseShellExecute = false,
                WorkingDirectory = appDirectory
            };

            foreach (string commandLineArgument in OS.GetCmdlineArgs())
            {
                startInfo.ArgumentList.Add(commandLineArgument);
            }

            Process.Start(startInfo);
            GD.Print($"[Rename] Started {newExecutablePath} in place of {legacyExecutablePath}.");
            return true;
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Rename] Could not move to the new file names: {exception.Message}");
            return false;
        }
    }

    // Deletes the old executable, .pck, launcher and .NET folder once the app runs under its new name.
    // The old process may still be exiting, so each file gets a few attempts.
    public static void RemoveLegacyFilesInBackground()
    {
        if (!AppliesToThisBuild || AppIdentity.IsRunningUnderLegacyName())
        {
            return;
        }

        string appDirectory = Path.GetDirectoryName(OS.GetExecutablePath());

        if (!HasLegacyFiles(appDirectory))
        {
            return;
        }

        // A Steam shortcut to the old executable keeps working through it until the user fixes
        // the shortcut from Settings or the startup offer, so the old files stay until then.
        string legacyExecutablePath = Path.Combine(appDirectory, AppIdentity.LegacyFileBaseName + AppIdentity.ExecutableExtension);

        if (SteamShortcuts.HasShortcutTo(legacyExecutablePath))
        {
            GD.Print($"[Rename] Steam still starts {legacyExecutablePath}, so the old files stay for now.");
            return;
        }

        Task.Run(async () =>
        {
            for (int attempt = 0; attempt < 10 && HasLegacyFiles(appDirectory); attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(1000);
                }

                TryDeleteLegacyFiles(appDirectory);
            }

            if (HasLegacyFiles(appDirectory))
            {
                GD.PrintErr($"[Rename] Some {AppIdentity.LegacyFileBaseName} files in {appDirectory} could not be deleted.");
            }

            else
            {
                GD.Print($"[Rename] Removed the {AppIdentity.LegacyFileBaseName} files from {appDirectory}.");
            }
        });
    }

    private static bool HasLegacyFiles(string appDirectory)
    {
        foreach (string fileName in LegacyFileNames)
        {
            if (File.Exists(Path.Combine(appDirectory, fileName)))
            {
                return true;
            }
        }

        return Directory.GetDirectories(appDirectory, $"data_{AppIdentity.LegacyFileBaseName}_*").Length > 0;
    }

    private static void TryDeleteLegacyFiles(string appDirectory)
    {
        foreach (string fileName in LegacyFileNames)
        {
            try
            {
                File.Delete(Path.Combine(appDirectory, fileName));
            }

            catch (Exception)
            {
            }
        }

        foreach (string legacyDataDirectory in Directory.GetDirectories(appDirectory, $"data_{AppIdentity.LegacyFileBaseName}_*"))
        {
            try
            {
                Directory.Delete(legacyDataDirectory, true);
            }

            catch (Exception)
            {
            }
        }
    }
}
