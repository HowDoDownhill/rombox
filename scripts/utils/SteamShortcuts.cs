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
    Updated,
    AlreadyAdded,
    SteamNotFound,
    Failed
}

public enum SteamShortcutState
{
    SteamNotFound,
    NotInLibrary,
    // A shortcut exists but points at an old executable path or carries the old name. Steam keys
    // artwork, Steam Input and play time on an id built from both, so it is rewritten.
    Outdated,
    Current
}

// Adds this app to Steam as a non-Steam game and starts it through Steam.
//
// Steam reads shortcuts.vdf and localconfig.vdf when it starts and writes both back when it exits,
// so every write here happens with Steam closed. Artwork files are read when the library loads.
public static class SteamShortcuts
{
    public const string ShortcutName = AppIdentity.DisplayName;

    private const long SteamId64Base = 76561197960265728;
    private const int SteamExitTimeoutMilliseconds = 30000;
    private const int SteamExitPollMilliseconds = 500;
    private const string BackupSuffix = ".rombox-backup";

    // Steam's per-game Steam Input setting: 0 off, 1 follow the global setting, 2 on.
    private const string SteamInputForcedOn = "2";

    private static readonly (string ResourcePath, string FileSuffix)[] Artwork =
    {
        ("res://assets/steam/capsule.png", "p.png"),
        ("res://assets/steam/wide_capsule.png", ".png"),
        ("res://assets/steam/hero.png", "_hero.png"),
        ("res://assets/steam/logo.png", "_logo.png"),
        ("res://assets/icons/rombox.png", "_icon.png"),
    };

    // The editor runs from Godot's executable, so it only takes part when pointed at a test Steam folder.
    public static bool IsSupportedPlatform =>
        (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) && !OS.HasFeature("android")
        && (!OS.HasFeature("editor") || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(SteamRootOverrideVariable)));

    public static bool IsLaunchedThroughSteam =>
        !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamGameId"))
        || !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId"));

    // Points every lookup at another Steam folder, for testing against a copy of userdata.
    private const string SteamRootOverrideVariable = "ROMBOX_STEAM_ROOT";
    private const string AddToSteamArgument = "--add-to-steam";

    public static string FindSteamRoot()
    {
        string overrideRoot = System.Environment.GetEnvironmentVariable(SteamRootOverrideVariable);

        if (!string.IsNullOrEmpty(overrideRoot))
        {
            return Directory.Exists(Path.Combine(overrideRoot, "userdata")) ? overrideRoot : null;
        }

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

    public static string FindAccountDirectory()
    {
        string steamRoot = FindSteamRoot();
        return steamRoot == null ? null : FindMostRecentAccountDirectory(steamRoot);
    }

    public static string FindShortcutsFile()
    {
        string accountDirectory = FindAccountDirectory();
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

    public static SteamShortcutState GetState()
    {
        if (!IsSupportedPlatform)
        {
            return SteamShortcutState.SteamNotFound;
        }

        string shortcutsPath = FindShortcutsFile();

        if (shortcutsPath == null)
        {
            return SteamShortcutState.SteamNotFound;
        }

        if (!File.Exists(shortcutsPath))
        {
            return SteamShortcutState.NotInLibrary;
        }

        try
        {
            List<BinaryVdfNode> root = ReadShortcuts(shortcutsPath);
            BinaryVdfNode current = FindCurrentEntry(root);

            if (current != null)
            {
                return IsUpToDate(current) ? SteamShortcutState.Current : SteamShortcutState.Outdated;
            }

            return FindOutdatedEntry(root) != null ? SteamShortcutState.Outdated : SteamShortcutState.NotInLibrary;
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not read {shortcutsPath}: {exception.Message}");
            return SteamShortcutState.NotInLibrary;
        }
    }

    // `rombox -- --add-to-steam` writes the shortcut, artwork and Steam Input setting, then quits.
    // Close Steam first, or it overwrites the files when it exits.
    public static bool RunCommandLineAddIfRequested()
    {
        if (!OS.GetCmdlineUserArgs().Contains(AddToSteamArgument))
        {
            return false;
        }

        if (!IsSupportedPlatform)
        {
            GD.PrintErr($"[Steam] {AddToSteamArgument} only works in a Windows or Linux build.");
            return true;
        }

        if (IsSteamRunning() && string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable(SteamRootOverrideVariable)))
        {
            GD.PrintErr($"[Steam] close Steam before {AddToSteamArgument}; Steam rewrites its shortcuts when it exits.");
            return true;
        }

        SteamShortcutResult result = AddOrUpdateThisApp(true);
        GD.Print($"[Steam] {AddToSteamArgument}: {result}, appid {FindShortAppId()}.");
        return true;
    }

    public static bool HasShortcutTo(string executablePath)
    {
        try
        {
            string shortcutsPath = IsSupportedPlatform ? FindShortcutsFile() : null;

            if (shortcutsPath == null || !File.Exists(shortcutsPath))
            {
                return false;
            }

            string normalisedPath = NormalisePath(executablePath);
            return EnumerateEntries(ReadShortcuts(shortcutsPath)).Any(shortcut => string.Equals(NormalisePath(shortcut.Child("Exe")?.StringValue), normalisedPath, PathComparison));
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not read Steam's shortcuts: {exception.Message}");
            return false;
        }
    }

    public static bool ContainsThisApp()
    {
        SteamShortcutState state = GetState();
        return state == SteamShortcutState.Current || state == SteamShortcutState.Outdated;
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

    // Starts this app's shortcut through Steam, which starts Steam first when it is closed.
    public static void LaunchThroughSteam()
    {
        ulong gameId = ((ulong)FindShortAppId() << 32) | 0x02000000;
        GD.Print($"[Steam] starting steam://rungameid/{gameId}.");
        OS.ShellOpen($"steam://rungameid/{gameId}");
    }

    // Adds the shortcut, or rewrites an outdated one, then writes artwork and the Steam Input
    // setting. Steam must be closed.
    public static SteamShortcutResult AddOrUpdateThisApp(bool enableSteamInput)
    {
        string accountDirectory = FindAccountDirectory();

        if (accountDirectory == null)
        {
            GD.Print("[Steam] no Steam install with a signed-in account was found.");
            return SteamShortcutResult.SteamNotFound;
        }

        string shortcutsPath = Path.Combine(accountDirectory, "config", "shortcuts.vdf");
        SteamShortcutResult result;

        try
        {
            result = WriteShortcut(shortcutsPath);
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not write a shortcut in {shortcutsPath}: {exception.Message}");
            return SteamShortcutResult.Failed;
        }

        uint shortAppId = FindShortAppId();
        WriteArtwork(accountDirectory, shortAppId);

        if (enableSteamInput)
        {
            EnableSteamInput(accountDirectory, shortAppId);
        }

        return result;
    }

    private static SteamShortcutResult WriteShortcut(string shortcutsPath)
    {
        List<BinaryVdfNode> root = File.Exists(shortcutsPath) ? ReadShortcuts(shortcutsPath) : new List<BinaryVdfNode>();
        BinaryVdfNode shortcuts = root.FirstOrDefault(node => node.Type == BinaryVdfNode.MapType && string.Equals(node.Key, "shortcuts", StringComparison.OrdinalIgnoreCase));

        if (shortcuts == null)
        {
            shortcuts = BinaryVdfNode.Map("shortcuts");
            root.Add(shortcuts);
        }

        BinaryVdfNode existing = FindCurrentEntry(root) ?? FindOutdatedEntry(root);
        SteamShortcutResult result;

        if (existing != null && IsUpToDate(existing))
        {
            GD.Print($"[Steam] {shortcutsPath} already has an up-to-date shortcut to this app.");
            return SteamShortcutResult.AlreadyAdded;
        }

        if (existing != null)
        {
            GD.Print($"[Steam] updating the shortcut \"{existing.Child("AppName")?.StringValue}\" -> {existing.Child("Exe")?.StringValue} to this app.");
            ApplyIdentity(existing);
            result = SteamShortcutResult.Updated;
        }

        else
        {
            int nextIndex = shortcuts.Children.Select(child => int.TryParse(child.Key, out int index) ? index + 1 : 0).DefaultIfEmpty(0).Max();
            shortcuts.Children.Add(BuildShortcut(nextIndex.ToString()));
            result = SteamShortcutResult.Added;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutsPath));

        if (File.Exists(shortcutsPath))
        {
            File.Copy(shortcutsPath, shortcutsPath + BackupSuffix, true);
        }

        File.WriteAllBytes(shortcutsPath, BinaryVdf.Write(root));
        GD.Print($"[Steam] {(result == SteamShortcutResult.Added ? "added" : "updated")} a shortcut to {OS.GetExecutablePath()} in {shortcutsPath}.");
        return result;
    }

    private static void WriteArtwork(string accountDirectory, uint shortAppId)
    {
        string gridDirectory = Path.Combine(accountDirectory, "config", "grid");

        try
        {
            Directory.CreateDirectory(gridDirectory);

            foreach ((string resourcePath, string fileSuffix) in Artwork)
            {
                Texture2D texture = ResourceLoader.Load<Texture2D>(resourcePath);
                Image image = texture?.GetImage();

                if (image == null)
                {
                    GD.PrintErr($"[Steam] {resourcePath} could not be loaded, so Steam gets no {fileSuffix} artwork.");
                    continue;
                }

                if (image.IsCompressed())
                {
                    image.Decompress();
                }

                image.SavePng(Path.Combine(gridDirectory, shortAppId + fileSuffix));
            }

            GD.Print($"[Steam] wrote artwork for {shortAppId} to {gridDirectory}.");
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not write artwork to {gridDirectory}: {exception.Message}");
        }
    }

    private static void EnableSteamInput(string accountDirectory, uint shortAppId)
    {
        string localConfigPath = Path.Combine(accountDirectory, "config", "localconfig.vdf");

        if (!File.Exists(localConfigPath))
        {
            GD.Print($"[Steam] {localConfigPath} does not exist yet, so Steam Input keeps Steam's default for this app.");
            return;
        }

        try
        {
            List<TextVdfNode> root = TextVdf.Parse(File.ReadAllText(localConfigPath));
            TextVdfNode store = root.FirstOrDefault(node => node.IsMap && string.Equals(node.Key, "UserLocalConfigStore", StringComparison.OrdinalIgnoreCase));

            if (store == null)
            {
                GD.PrintErr($"[Steam] {localConfigPath} has no UserLocalConfigStore block, so it was left alone.");
                return;
            }

            TextVdfNode appSettings = store.GetOrAddMap("apps").GetOrAddMap(shortAppId.ToString());

            if (appSettings.Child("UseSteamControllerConfig")?.Value == SteamInputForcedOn)
            {
                return;
            }

            appSettings.SetValue("UseSteamControllerConfig", SteamInputForcedOn);
            File.Copy(localConfigPath, localConfigPath + BackupSuffix, true);
            File.WriteAllText(localConfigPath, TextVdf.Write(root), new UTF8Encoding(false));
            GD.Print($"[Steam] turned Steam Input on for {shortAppId} in {localConfigPath}.");
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not turn Steam Input on in {localConfigPath}: {exception.Message}");
        }
    }

    private static List<BinaryVdfNode> ReadShortcuts(string shortcutsPath)
    {
        return BinaryVdf.Read(File.ReadAllBytes(shortcutsPath));
    }

    private static IEnumerable<BinaryVdfNode> EnumerateEntries(List<BinaryVdfNode> root)
    {
        BinaryVdfNode shortcuts = root.FirstOrDefault(node => string.Equals(node.Key, "shortcuts", StringComparison.OrdinalIgnoreCase));
        return shortcuts?.Children ?? Enumerable.Empty<BinaryVdfNode>();
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static BinaryVdfNode FindCurrentEntry(List<BinaryVdfNode> root)
    {
        string executablePath = NormalisePath(OS.GetExecutablePath());
        return EnumerateEntries(root).FirstOrDefault(shortcut => string.Equals(NormalisePath(shortcut.Child("Exe")?.StringValue), executablePath, PathComparison));
    }

    // A shortcut this app made before it moved or was renamed: either the old executable name in
    // this folder, or an entry under this app's name whose executable no longer exists. A second
    // copy of the app that still exists elsewhere keeps its own shortcut.
    private static BinaryVdfNode FindOutdatedEntry(List<BinaryVdfNode> root)
    {
        string appDirectory = Path.GetDirectoryName(NormalisePath(OS.GetExecutablePath()));
        string legacyExecutablePath = Path.Combine(appDirectory, AppIdentity.LegacyFileBaseName + AppIdentity.ExecutableExtension);

        return EnumerateEntries(root).FirstOrDefault(shortcut =>
        {
            string shortcutExecutable = NormalisePath(shortcut.Child("Exe")?.StringValue);
            string shortcutName = shortcut.Child("AppName")?.StringValue ?? "";

            if (string.Equals(shortcutExecutable, legacyExecutablePath, PathComparison))
            {
                return true;
            }

            bool carriesThisAppsName = string.Equals(shortcutName, AppIdentity.DisplayName, StringComparison.OrdinalIgnoreCase) || IsLegacyName(shortcutName);
            string executableName = Path.GetFileNameWithoutExtension(shortcutExecutable);
            bool isThisAppsExecutable = string.Equals(executableName, AppIdentity.FileBaseName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(executableName, AppIdentity.LegacyFileBaseName, StringComparison.OrdinalIgnoreCase);

            return carriesThisAppsName && isThisAppsExecutable && !File.Exists(shortcutExecutable);
        });
    }

    // A name the user gave the shortcut is kept; only the pre-rename name is replaced.
    private static bool IsUpToDate(BinaryVdfNode shortcut)
    {
        string executablePath = NormalisePath(OS.GetExecutablePath());
        string shortcutName = shortcut.Child("AppName")?.StringValue;

        return string.Equals(NormalisePath(shortcut.Child("Exe")?.StringValue), executablePath, PathComparison)
            && !string.IsNullOrEmpty(shortcutName)
            && !IsLegacyName(shortcutName)
            && ReadInt32(shortcut.Child("appid")) != null;
    }

    private static string NameToKeep(BinaryVdfNode shortcut)
    {
        string shortcutName = shortcut.Child("AppName")?.StringValue;
        return string.IsNullOrEmpty(shortcutName) || IsLegacyName(shortcutName) ? ShortcutName : shortcutName;
    }

    private static bool IsLegacyName(string shortcutName)
    {
        return string.Equals(shortcutName, AppIdentity.LegacyDisplayName, StringComparison.OrdinalIgnoreCase);
    }

    // Steam keys artwork, localconfig.vdf and rungameid on the appid stored in the shortcut. Shortcuts
    // this app writes store the computed id. Steam gives shortcuts added by hand an id of its own
    // (checked against three on a Windows client: none matched the CRC).
    public static uint FindShortAppId()
    {
        try
        {
            string shortcutsPath = FindShortcutsFile();

            if (shortcutsPath != null && File.Exists(shortcutsPath))
            {
                BinaryVdfNode current = FindCurrentEntry(ReadShortcuts(shortcutsPath));
                int? storedAppId = ReadInt32(current?.Child("appid"));

                if (storedAppId != null)
                {
                    return unchecked((uint)storedAppId.Value);
                }

                if (current != null)
                {
                    return ComputeShortAppId(QuotedExecutablePath, NameToKeep(current));
                }
            }
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[Steam] could not read the shortcut's appid: {exception.Message}");
        }

        return CurrentShortAppId();
    }

    private static int? ReadInt32(BinaryVdfNode node)
    {
        return node?.RawValue != null && node.RawValue.Length == 4 ? BitConverter.ToInt32(node.RawValue, 0) : null;
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

    private static string QuotedExecutablePath => $"\"{NormalisePath(OS.GetExecutablePath())}\"";

    private static string IconPath(uint shortAppId)
    {
        string accountDirectory = FindAccountDirectory();
        return accountDirectory == null ? "" : Path.Combine(accountDirectory, "config", "grid", shortAppId + "_icon.png");
    }

    // Points an existing entry at this executable, keeping its appid (and with it Steam's play time,
    // artwork and controller settings), tags, launch options and any name the user chose.
    private static void ApplyIdentity(BinaryVdfNode shortcut)
    {
        string executablePath = NormalisePath(OS.GetExecutablePath());
        string name = NameToKeep(shortcut);
        int? storedAppId = ReadInt32(shortcut.Child("appid"));
        uint shortAppId = storedAppId != null ? unchecked((uint)storedAppId.Value) : ComputeShortAppId(QuotedExecutablePath, name);

        SetOrAdd(shortcut, BinaryVdfNode.Int32("appid", unchecked((int)shortAppId)));
        SetOrAdd(shortcut, BinaryVdfNode.String("AppName", name));
        SetOrAdd(shortcut, BinaryVdfNode.String("Exe", QuotedExecutablePath));
        SetOrAdd(shortcut, BinaryVdfNode.String("StartDir", Path.GetDirectoryName(executablePath) + Path.DirectorySeparatorChar));
        SetOrAdd(shortcut, BinaryVdfNode.String("icon", IconPath(shortAppId)));
    }

    private static void SetOrAdd(BinaryVdfNode parent, BinaryVdfNode replacement)
    {
        int index = parent.Children.FindIndex(child => string.Equals(child.Key, replacement.Key, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            parent.Children[index] = replacement;
        }

        else
        {
            parent.Children.Add(replacement);
        }
    }

    private static BinaryVdfNode BuildShortcut(string index)
    {
        string executablePath = NormalisePath(OS.GetExecutablePath());
        string startDirectory = Path.GetDirectoryName(executablePath) + Path.DirectorySeparatorChar;
        uint shortAppId = CurrentShortAppId();

        return BinaryVdfNode.Map(index, new[]
        {
            BinaryVdfNode.Int32("appid", unchecked((int)shortAppId)),
            BinaryVdfNode.String("AppName", ShortcutName),
            BinaryVdfNode.String("Exe", QuotedExecutablePath),
            BinaryVdfNode.String("StartDir", startDirectory),
            BinaryVdfNode.String("icon", IconPath(shortAppId)),
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

    // The 32-bit id Steam gives a non-Steam game: CRC32 of the quoted executable path followed by
    // the name, with the top bit set. Artwork, localconfig.vdf and rungameid all use it.
    public static uint CurrentShortAppId()
    {
        return ComputeShortAppId(QuotedExecutablePath, ShortcutName);
    }

    private static uint ComputeShortAppId(string quotedExecutable, string appName)
    {
        return Crc32(Encoding.UTF8.GetBytes(quotedExecutable + appName)) | 0x80000000;
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
