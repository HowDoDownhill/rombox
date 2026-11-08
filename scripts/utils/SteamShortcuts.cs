using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public enum SteamShortcutResult
{
    Added,
    AlreadyAdded,
    SteamNotFound,
    Failed
}

public static class SteamShortcuts
{
    public const string ShortcutName = "RomM Frontend";

    private const long SteamId64Base = 76561197960265728;
    private const int SteamExitTimeoutMilliseconds = 30000;
    private const int SteamExitPollMilliseconds = 500;

    public static bool IsSupportedPlatform => (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && !OS.HasFeature("android");

    public static string FindSteamRoot()
    {
        foreach (string candidate in EnumerateSteamRootCandidates())
        {
            if (!string.IsNullOrEmpty(candidate) && Directory.Exists(Path.Combine(candidate, "userdata")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateSteamRootCandidates()
    {
        if (OperatingSystem.IsWindows())
        {
            string registryPath = null;

            try
            {
                using var steamKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                registryPath = steamKey?.GetValue("SteamPath") as string;
            }
            catch (Exception exception)
            {
                GD.PrintErr($"[Steam] could not read the Steam install path: {exception.Message}");
            }

            yield return registryPath?.Replace('/', '\\');
            yield return Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFilesX86), "Steam");
            yield break;
        }

        string home = System.Environment.GetEnvironmentVariable("HOME") ?? "";
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
    }

    public static string FindShortcutsFile()
    {
        string steamRoot = FindSteamRoot();

        if (steamRoot == null)
        {
            return null;
        }

        string accountDirectory = FindMostRecentAccountDirectory(steamRoot);
        return accountDirectory == null ? null : Path.Combine(accountDirectory, "config", "shortcuts.vdf");
    }

    private static string FindMostRecentAccountDirectory(string steamRoot)
    {
        string userdataDirectory = Path.Combine(steamRoot, "userdata");
        string loginUsersPath = Path.Combine(steamRoot, "config", "loginusers.vdf");

        if (File.Exists(loginUsersPath))
        {
            foreach (Match user in Regex.Matches(File.ReadAllText(loginUsersPath), "\"(\\d{17})\"\\s*\\{([^}]*)\\}"))
            {
                if (Regex.IsMatch(user.Groups[2].Value, "\"MostRecent\"\\s*\"1\"") && long.TryParse(user.Groups[1].Value, out long steamId64))
                {
                    string accountDirectory = Path.Combine(userdataDirectory, (steamId64 - SteamId64Base).ToString());

                    if (Directory.Exists(accountDirectory))
                    {
                        return accountDirectory;
                    }
                }
            }
        }

        return Directory.EnumerateDirectories(userdataDirectory)
            .Where(directory => long.TryParse(Path.GetFileName(directory), out long accountId) && accountId > 0)
            .OrderByDescending(directory => Directory.GetLastWriteTimeUtc(Path.Combine(directory, "config")))
            .FirstOrDefault();
    }

    public static bool ContainsThisApp()
    {
        string shortcutsPath = FindShortcutsFile();
        return shortcutsPath != null && File.Exists(shortcutsPath) && FindThisApp(ReadShortcuts(shortcutsPath)) != null;
    }

    public static bool IsSteamRunning()
    {
        return Process.GetProcessesByName("steam").Length > 0;
    }

    public static async Task CloseSteam()
    {
        OS.ShellOpen("steam://exit");
        int elapsedMilliseconds = 0;

        while (IsSteamRunning() && elapsedMilliseconds < SteamExitTimeoutMilliseconds)
        {
            await Task.Delay(SteamExitPollMilliseconds);
            elapsedMilliseconds += SteamExitPollMilliseconds;
        }

        GD.Print($"[Steam] Steam {(IsSteamRunning() ? "is still running" : "closed")} after {elapsedMilliseconds} ms.");
    }

    public static void OpenSteamLibrary()
    {
        OS.ShellOpen("steam://open/games");
    }

    public static SteamShortcutResult AddThisApp()
    {
        string shortcutsPath = FindShortcutsFile();

        if (shortcutsPath == null)
        {
            GD.Print("[Steam] no Steam install with a signed-in account was found.");
            return SteamShortcutResult.SteamNotFound;
        }

        try
        {
            List<BinaryVdfNode> root = File.Exists(shortcutsPath) ? ReadShortcuts(shortcutsPath) : new List<BinaryVdfNode>();
            BinaryVdfNode shortcuts = root.FirstOrDefault(node => node.Type == BinaryVdfNode.MapType && string.Equals(node.Key, "shortcuts", StringComparison.OrdinalIgnoreCase));

            if (shortcuts == null)
            {
                shortcuts = BinaryVdfNode.Map("shortcuts");
                root.Add(shortcuts);
            }

            if (FindThisApp(root) != null)
            {
                GD.Print($"[Steam] {shortcutsPath} already has a shortcut to this app.");
                return SteamShortcutResult.AlreadyAdded;
            }

            int nextIndex = shortcuts.Children.Select(child => int.TryParse(child.Key, out int index) ? index + 1 : 0).DefaultIfEmpty(0).Max();
            shortcuts.Children.Add(BuildShortcut(nextIndex.ToString()));

            Directory.CreateDirectory(Path.GetDirectoryName(shortcutsPath));

            if (File.Exists(shortcutsPath))
            {
                File.Copy(shortcutsPath, shortcutsPath + ".romm-backup", true);
            }

            File.WriteAllBytes(shortcutsPath, BinaryVdf.Write(root));
            GD.Print($"[Steam] added a shortcut to {OS.GetExecutablePath()} in {shortcutsPath}.");
            return SteamShortcutResult.Added;
        }
        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not add a shortcut in {shortcutsPath}: {exception.Message}");
            return SteamShortcutResult.Failed;
        }
    }

    private static List<BinaryVdfNode> ReadShortcuts(string shortcutsPath)
    {
        return BinaryVdf.Read(File.ReadAllBytes(shortcutsPath));
    }

    private static BinaryVdfNode FindThisApp(List<BinaryVdfNode> root)
    {
        string executablePath = OS.GetExecutablePath();
        StringComparison pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        BinaryVdfNode shortcuts = root.FirstOrDefault(node => string.Equals(node.Key, "shortcuts", StringComparison.OrdinalIgnoreCase));

        return shortcuts?.Children.FirstOrDefault(shortcut =>
            string.Equals(NormalisePath(shortcut.Child("Exe")?.StringValue), NormalisePath(executablePath), pathComparison));
    }

    private static string NormalisePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        string unquoted = path.Trim().Trim('"');
        return OperatingSystem.IsWindows() ? unquoted.Replace('/', '\\') : unquoted;
    }

    private static BinaryVdfNode BuildShortcut(string index)
    {
        string executablePath = NormalisePath(OS.GetExecutablePath());
        string startDirectory = Path.GetDirectoryName(executablePath) + Path.DirectorySeparatorChar;
        string quotedExecutable = $"\"{executablePath}\"";

        return BinaryVdfNode.Map(index, new[]
        {
            BinaryVdfNode.Int32("appid", ComputeShortcutAppId(quotedExecutable, ShortcutName)),
            BinaryVdfNode.String("AppName", ShortcutName),
            BinaryVdfNode.String("Exe", quotedExecutable),
            BinaryVdfNode.String("StartDir", startDirectory),
            BinaryVdfNode.String("icon", ""),
            BinaryVdfNode.String("ShortcutPath", ""),
            BinaryVdfNode.String("LaunchOptions", ""),
            BinaryVdfNode.Int32("IsHidden", 0),
            BinaryVdfNode.Int32("AllowDesktopConfig", 1),
            BinaryVdfNode.Int32("AllowOverlay", 1),
            BinaryVdfNode.Int32("OpenVR", 0),
            BinaryVdfNode.Int32("Devkit", 0),
            BinaryVdfNode.String("DevkitGameID", ""),
            BinaryVdfNode.Int32("DevkitOverrideAppID", 0),
            BinaryVdfNode.Int32("LastPlayTime", 0),
            BinaryVdfNode.String("FlatpakAppID", ""),
            BinaryVdfNode.String("sortas", ""),
            BinaryVdfNode.Map("tags")
        });
    }

    private static int ComputeShortcutAppId(string quotedExecutable, string appName)
    {
        uint checksum = Crc32(Encoding.UTF8.GetBytes(quotedExecutable + appName));
        return unchecked((int)(checksum | 0x80000000));
    }

    private static uint Crc32(byte[] data)
    {
        uint checksum = 0xFFFFFFFF;

        foreach (byte value in data)
        {
            checksum ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                checksum = (checksum & 1) != 0 ? (checksum >> 1) ^ 0xEDB88320 : checksum >> 1;
            }
        }

        return ~checksum;
    }
}
