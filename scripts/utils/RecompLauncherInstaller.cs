using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

// Puts RecompLauncher inside RomBox's own folder as a portable copy: the program, a marker file
// that tells it to keep its data beside itself, and nothing in the user's profile.
public static class RecompLauncherInstaller
{
    private const string ReleasesUrl = "https://api.github.com/repos/elripalda/RecompLauncher/releases?per_page=10";
    private const string WindowsAssetSuffix = "_x64-setup.exe";
    private const string LinuxAssetSuffix = "_amd64.AppImage";
    private const string PortableMarkerFileName = "portable.txt";
    private const string PortableMarkerText = "RecompLauncher keeps its data in the data folder next to it while this file is here.";
    private const string WindowsExecutableName = "recomplauncher.exe";
    private const string LinuxExecutableName = "recomplauncher.AppImage";
    private const string SystemDataFolderName = "com.recomplauncher.app";

    // Left behind when a portable copy is made: both are rebuilt or downloaded again.
    private static readonly string[] DataFoldersLeftBehind = { "cache", "EBWebView" };

    private static readonly System.Net.Http.HttpClient httpClient = CreateHttpClient();

    private static System.Net.Http.HttpClient CreateHttpClient()
    {
        var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RomBox/1.0");
        return client;
    }

    public static bool IsSupportedHere => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    public static bool HasCopyInsideRomBox(ConfigManager configManager)
    {
        string executablePath = RecompLauncher.FindExecutable(configManager);
        return executablePath != null && IsInside(executablePath, RecompLauncher.ManagedInstallDirectory(configManager));
    }

    // A copy installed on this computer the usual way, which could be moved into RomBox.
    public static bool CanMoveIntoRomBox(ConfigManager configManager)
    {
        return OperatingSystem.IsWindows() && !HasCopyInsideRomBox(configManager) && RecompLauncher.FindExecutable(configManager) != null;
    }

    private static bool IsInside(string path, string folder)
    {
        string fullPath = Path.GetFullPath(path).Replace('\\', '/');
        string fullFolder = Path.GetFullPath(folder).Replace('\\', '/').TrimEnd('/') + "/";
        return fullPath.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase);
    }

    // Downloads the newest RecompLauncher from its GitHub releases into RomBox's folder.
    public static async Task<(bool Succeeded, string Error)> InstallAsync(AppInstance appInstance, Action<string> reportStatus)
    {
        ConfigManager configManager = appInstance.configManager;
        string managedDirectory = RecompLauncher.ManagedInstallDirectory(configManager);

        try
        {
            reportStatus("Looking for the newest RecompLauncher...");
            string assetSuffix = OperatingSystem.IsWindows() ? WindowsAssetSuffix : LinuxAssetSuffix;
            GithubReleaseAsset asset = await FindNewestAssetAsync(assetSuffix);

            if (asset == null)
            {
                return (false, "No RecompLauncher download was found. Check your connection and try again.");
            }

            Directory.CreateDirectory(managedDirectory);
            File.WriteAllText(Path.Combine(managedDirectory, ".gdignore"), "");
            string downloadFilePath = Path.Combine(managedDirectory, asset.Name);

            reportStatus("Downloading RecompLauncher...");

            if (!await UniversalInstaller.DownloadFileAsync(appInstance, asset.BrowserDownloadUrl, downloadFilePath, "RecompLauncher"))
            {
                return (false, "The download did not finish.");
            }

            reportStatus("Setting RecompLauncher up...");

            if (OperatingSystem.IsWindows())
            {
                bool unpacked = await UnpackWindowsSetupAsync(configManager, downloadFilePath, managedDirectory);
                File.Delete(downloadFilePath);

                if (!unpacked || !File.Exists(Path.Combine(managedDirectory, WindowsExecutableName)))
                {
                    return (false, "The download could not be unpacked.");
                }

                File.WriteAllText(Path.Combine(managedDirectory, PortableMarkerFileName), PortableMarkerText);
            }

            else
            {
                string executablePath = Path.Combine(managedDirectory, LinuxExecutableName);
                File.Move(downloadFilePath, executablePath, true);
                Process.Start("chmod", $"+x \"{executablePath}\"")?.WaitForExit();
            }

            Godot.GD.Print($"[Recomps] installed RecompLauncher ({asset.Name}) into {managedDirectory}.");
            return (true, null);
        }

        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private static async Task<GithubReleaseAsset> FindNewestAssetAsync(string assetSuffix)
    {
        using var response = await httpClient.GetAsync(ReleasesUrl);

        if (!response.IsSuccessStatusCode)
        {
            Godot.GD.PrintErr($"[Recomps] could not list RecompLauncher's releases: {response.StatusCode}");
            return null;
        }

        var releases = JsonSerializer.Deserialize(await response.Content.ReadAsStringAsync(), RommJsonContext.Default.ListGithubReleaseInfo);

        return releases?
            .Where(release => !release.IsDraft && release.Assets != null)
            .Select(release => release.Assets.FirstOrDefault(candidate => candidate.Name != null && candidate.Name.EndsWith(assetSuffix, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(candidate => candidate != null);
    }

    // RecompLauncher's Windows download is a setup program. Its files are taken out with 7-Zip,
    // so nothing is installed into Windows itself.
    private static Task<bool> UnpackWindowsSetupAsync(ConfigManager configManager, string setupFilePath, string destinationDirectory)
    {
        string archiveToolPath = Path.Combine(configManager.ApplicationRootDirectory, "tools", "7zip", "windows", "full", "7z.exe");

        return Task.Run(() =>
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = archiveToolPath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (string argument in new[] { "x", "-y", $"-o{destinationDirectory}", setupFilePath, "-x!$PLUGINSDIR", "-x!uninstall.exe*" })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process archiveTool = Process.Start(startInfo);
            archiveTool.WaitForExit();
            return archiveTool.ExitCode == 0;
        });
    }

    // Copies a RecompLauncher installed the usual way into RomBox's folder, with what it remembers:
    // its library, settings, covers and save copies. Installed recomps stay where they are and keep
    // working. The old copy is left in place.
    public static (bool Succeeded, string Error) MoveIntoRomBox(ConfigManager configManager)
    {
        if (RecompLauncher.IsRunning())
        {
            return (false, "Close RecompLauncher first.");
        }

        string sourceExecutablePath = RecompLauncher.FindExecutable(configManager);
        string sourceDataDirectory = RecompLauncher.FindDataDirectory(configManager);
        string managedDirectory = RecompLauncher.ManagedInstallDirectory(configManager);

        if (sourceExecutablePath == null || IsInside(sourceExecutablePath, managedDirectory))
        {
            return (false, "There is no other copy of RecompLauncher to move.");
        }

        try
        {
            Directory.CreateDirectory(managedDirectory);
            File.WriteAllText(Path.Combine(managedDirectory, ".gdignore"), "");
            File.Copy(sourceExecutablePath, Path.Combine(managedDirectory, WindowsExecutableName), true);

            if (sourceDataDirectory != null && Directory.Exists(sourceDataDirectory))
            {
                CopyData(sourceDataDirectory, Path.Combine(managedDirectory, "data"), true);

                // The list of recomps is kept, so RomBox can match games before the launcher is next opened.
                string catalogFilePath = Path.Combine(sourceDataDirectory, "cache", "pc-catalog.json");

                if (File.Exists(catalogFilePath))
                {
                    Directory.CreateDirectory(Path.Combine(managedDirectory, "data", "cache"));
                    File.Copy(catalogFilePath, Path.Combine(managedDirectory, "data", "cache", "pc-catalog.json"), true);
                }
            }

            File.WriteAllText(Path.Combine(managedDirectory, PortableMarkerFileName), PortableMarkerText);
            Godot.GD.Print($"[Recomps] copied RecompLauncher from {Path.GetDirectoryName(sourceExecutablePath)} into {managedDirectory}.");
            return (true, null);
        }

        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private static void CopyData(string sourceDirectory, string destinationDirectory, bool isTopLevel)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFilePath in Directory.GetFiles(sourceDirectory))
        {
            File.Copy(sourceFilePath, Path.Combine(destinationDirectory, Path.GetFileName(sourceFilePath)), true);
        }

        foreach (string sourceSubdirectory in Directory.GetDirectories(sourceDirectory))
        {
            string folderName = Path.GetFileName(sourceSubdirectory);

            if (isTopLevel && DataFoldersLeftBehind.Contains(folderName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            CopyData(sourceSubdirectory, Path.Combine(destinationDirectory, folderName), false);
        }
    }
}
