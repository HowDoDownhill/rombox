using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

// One recomp installed through RecompLauncher, joined with what its catalog says about it.
public class InstalledRecomp
{
    public string Id { get; set; }

    // The recomp project's own name, such as "Zelda 64: Recompiled".
    public string ProjectName { get; set; }

    // The original game's name, when the launcher knows it.
    public string GameName { get; set; }

    // RecompLauncher's id for the console the game came from, such as n64 or x360.
    public string Origin { get; set; }

    public string CoverUrl { get; set; }

    public string LogoUrl { get; set; }

    public string Summary { get; set; }

    public string Author { get; set; }

    public string ReleaseYear { get; set; }
}

// One recomp in RecompLauncher's catalog, installed or not.
public class CatalogRecomp
{
    public string Id { get; set; }

    public string ProjectName { get; set; }

    public string GameName { get; set; }

    public string Origin { get; set; }
}

// RecompLauncher (github.com/elripalda/RecompLauncher) installs, sets up, updates and starts
// recomps. RomBox reads its library file to list what is installed and starts games through it.
public static class RecompLauncher
{
    private const string DataFolderName = "com.recomplauncher.app";
    private const string WindowsUninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RecompLauncher";
    private const string WindowsLinkCommandKeyPath = @"Software\Classes\recomplauncher\shell\open\command";

    private static readonly string[] LinuxExecutableCandidates = { "/usr/bin/recomplauncher", "/usr/local/bin/recomplauncher" };

    public static string ManagedInstallDirectory(ConfigManager configManager)
    {
        return Path.Combine(configManager.ApplicationRootDirectory, "recomplauncher");
    }

    public static string FindExecutable(ConfigManager configManager)
    {
        string managedDirectory = ManagedInstallDirectory(configManager);

        if (Directory.Exists(managedDirectory))
        {
            string managedExecutable = Directory.GetFiles(managedDirectory)
                .FirstOrDefault(filePath => Path.GetFileName(filePath).StartsWith("recomplauncher", StringComparison.OrdinalIgnoreCase)
                    && (OperatingSystem.IsWindows() ? filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) : !filePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)));

            if (managedExecutable != null)
            {
                return managedExecutable;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            return FindWindowsInstall();
        }

        return LinuxExecutableCandidates.FirstOrDefault(File.Exists);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string FindWindowsInstall()
    {
        using (var uninstallKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsUninstallKeyPath))
        {
            string installLocation = (uninstallKey?.GetValue("InstallLocation") as string)?.Trim('"');

            if (!string.IsNullOrEmpty(installLocation) && File.Exists(Path.Combine(installLocation, "recomplauncher.exe")))
            {
                return Path.Combine(installLocation, "recomplauncher.exe");
            }
        }

        using (var linkCommandKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(WindowsLinkCommandKeyPath))
        {
            string linkCommand = linkCommandKey?.GetValue("") as string ?? "";
            string[] quotedParts = linkCommand.Split('"');
            string linkedExecutable = quotedParts.Length > 1 ? quotedParts[1] : null;

            return !string.IsNullOrEmpty(linkedExecutable) && File.Exists(linkedExecutable) ? linkedExecutable : null;
        }
    }

    public static bool IsInstalled(ConfigManager configManager) => FindExecutable(configManager) != null;

    // A portable copy has a marker file beside it and keeps everything in its own folder.
    private static bool IsPortable(string executablePath)
    {
        string executableDirectory = Path.GetDirectoryName(executablePath);
        return File.Exists(Path.Combine(executableDirectory, "portable")) || File.Exists(Path.Combine(executableDirectory, "portable.txt"));
    }

    // The launcher keeps its data beside itself when a portable marker is there, and in the
    // user's data folder otherwise.
    public static string FindDataDirectory(ConfigManager configManager)
    {
        string executablePath = FindExecutable(configManager);

        if (executablePath == null)
        {
            return null;
        }

        string executableDirectory = Path.GetDirectoryName(executablePath);

        if (File.Exists(Path.Combine(executableDirectory, "portable")) || File.Exists(Path.Combine(executableDirectory, "portable.txt")))
        {
            return Path.Combine(executableDirectory, "data");
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), DataFolderName);
        }

        string dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");

        if (string.IsNullOrEmpty(dataHome))
        {
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        return Path.Combine(dataHome, DataFolderName);
    }

    public static bool IsRunning()
    {
        try
        {
            Process[] launcherProcesses = Process.GetProcessesByName("recomplauncher");
            bool isRunning = launcherProcesses.Length > 0;

            foreach (Process launcherProcess in launcherProcesses)
            {
                launcherProcess.Dispose();
            }

            return isRunning;
        }

        catch
        {
            return false;
        }
    }

    private static string cachedLibraryStamp;
    private static List<InstalledRecomp> cachedInstalledRecomps = new List<InstalledRecomp>();

    public static List<InstalledRecomp> ReadInstalledRecomps(ConfigManager configManager)
    {
        string dataDirectory = FindDataDirectory(configManager);
        string libraryFilePath = dataDirectory == null ? null : Path.Combine(dataDirectory, "library.json");

        if (libraryFilePath == null || !File.Exists(libraryFilePath))
        {
            return new List<InstalledRecomp>();
        }

        string catalogFilePath = Path.Combine(dataDirectory, "cache", "pc-catalog.json");
        string libraryStamp = $"{libraryFilePath}|{File.GetLastWriteTimeUtc(libraryFilePath).Ticks}|{(File.Exists(catalogFilePath) ? File.GetLastWriteTimeUtc(catalogFilePath).Ticks : 0)}";

        if (libraryStamp == cachedLibraryStamp)
        {
            return cachedInstalledRecomps;
        }

        var installedRecomps = new List<InstalledRecomp>();

        try
        {
            using JsonDocument libraryDocument = JsonDocument.Parse(File.ReadAllBytes(libraryFilePath));

            if (libraryDocument.RootElement.TryGetProperty("installed", out JsonElement installedElement) && installedElement.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty installedEntry in installedElement.EnumerateObject())
                {
                    installedRecomps.Add(new InstalledRecomp
                    {
                        Id = installedEntry.Name,
                        ProjectName = ReadString(installedEntry.Value, "name") ?? installedEntry.Name,
                        GameName = ReadString(installedEntry.Value, "game")
                    });
                }
            }

            if (installedRecomps.Count > 0 && File.Exists(catalogFilePath))
            {
                FillFromCatalog(installedRecomps, catalogFilePath);
            }
        }

        catch (Exception exception)
        {
            Godot.GD.PrintErr($"[Recomps] could not read RecompLauncher's library: {exception.Message}");
            return cachedInstalledRecomps;
        }

        cachedLibraryStamp = libraryStamp;
        cachedInstalledRecomps = installedRecomps;
        Godot.GD.Print($"[Recomps] RecompLauncher has {installedRecomps.Count} recomp(s) installed.");
        return installedRecomps;
    }

    private static void FillFromCatalog(List<InstalledRecomp> installedRecomps, string catalogFilePath)
    {
        using JsonDocument catalogDocument = JsonDocument.Parse(File.ReadAllBytes(catalogFilePath));

        if (!catalogDocument.RootElement.TryGetProperty("apps", out JsonElement appsElement) || appsElement.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var recompsById = installedRecomps.ToDictionary(recomp => recomp.Id);

        foreach (JsonElement appElement in appsElement.EnumerateArray())
        {
            string appId = ReadString(appElement, "id");

            if (appId == null || !recompsById.TryGetValue(appId, out InstalledRecomp recomp))
            {
                continue;
            }

            recomp.Origin = ReadString(appElement, "origin");
            recomp.GameName ??= ReadString(appElement, "game");
            recomp.CoverUrl = ReadString(appElement, "cover");
            recomp.LogoUrl = ReadString(appElement, "logo");
            recomp.Author = ReadString(appElement, "author");
            recomp.Summary = ReadString(appElement, "blurb");

            if (appElement.TryGetProperty("about", out JsonElement aboutElement) && aboutElement.ValueKind == JsonValueKind.Object)
            {
                recomp.Summary = ReadString(aboutElement, "summary") ?? recomp.Summary;
                string releaseDate = ReadString(aboutElement, "released");
                recomp.ReleaseYear = releaseDate != null && releaseDate.Length >= 4 ? releaseDate.Substring(0, 4) : null;
            }
        }
    }

    private static string cachedCatalogStamp;
    private static List<CatalogRecomp> cachedCatalog = new List<CatalogRecomp>();

    // Every recomp in RecompLauncher's catalog that has a build for this computer. The launcher
    // keeps the catalog on disk and refreshes it itself.
    public static List<CatalogRecomp> ReadCatalog(ConfigManager configManager)
    {
        string dataDirectory = FindDataDirectory(configManager);
        string catalogFilePath = dataDirectory == null ? null : Path.Combine(dataDirectory, "cache", "pc-catalog.json");

        if (catalogFilePath == null || !File.Exists(catalogFilePath))
        {
            return new List<CatalogRecomp>();
        }

        string catalogStamp = $"{catalogFilePath}|{File.GetLastWriteTimeUtc(catalogFilePath).Ticks}";

        if (catalogStamp == cachedCatalogStamp)
        {
            return cachedCatalog;
        }

        var catalog = new List<CatalogRecomp>();
        string operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";

        try
        {
            using JsonDocument catalogDocument = JsonDocument.Parse(File.ReadAllBytes(catalogFilePath));

            if (catalogDocument.RootElement.TryGetProperty("apps", out JsonElement appsElement) && appsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement appElement in appsElement.EnumerateArray())
                {
                    string appId = ReadString(appElement, "id");
                    string gameName = ReadString(appElement, "game");
                    string origin = ReadString(appElement, "origin");

                    bool runsHere = appElement.TryGetProperty("systems", out JsonElement systemsElement)
                        && systemsElement.ValueKind == JsonValueKind.Array
                        && systemsElement.EnumerateArray().Any(systemElement => systemElement.ValueKind == JsonValueKind.String && systemElement.GetString() == operatingSystem);

                    if (appId == null || gameName == null || origin == null || !runsHere)
                    {
                        continue;
                    }

                    catalog.Add(new CatalogRecomp
                    {
                        Id = appId,
                        ProjectName = ReadString(appElement, "name") ?? appId,
                        GameName = gameName,
                        Origin = origin
                    });
                }
            }
        }

        catch (Exception exception)
        {
            Godot.GD.PrintErr($"[Recomps] could not read RecompLauncher's catalog: {exception.Message}");
            return cachedCatalog;
        }

        cachedCatalogStamp = catalogStamp;
        cachedCatalog = catalog;
        return catalog;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out JsonElement value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrEmpty(value.GetString())
            ? value.GetString()
            : null;
    }

    // Started with --play, the launcher process stays open until the game closes.
    public static ProcessStartInfo BuildPlayStartInfo(ConfigManager configManager, string recompId)
    {
        return BuildStartInfo(configManager, "--play", recompId);
    }

    // With a recomp id, the launcher opens on that recomp's page.
    public static ProcessStartInfo BuildBigPictureStartInfo(ConfigManager configManager, string recompId = null)
    {
        return string.IsNullOrEmpty(recompId)
            ? BuildStartInfo(configManager, "--bigpicture")
            : BuildStartInfo(configManager, "--bigpicture", $"recomplauncher://game/{recompId}");
    }

    // Removes a recomp: its folder goes, and so does its entry in the launcher's library. Only
    // while the launcher is closed, and only for a folder inside one of the launcher's own games
    // folders. Saves a recomp keeps elsewhere stay.
    public static (bool Succeeded, string Error) UninstallRecomp(ConfigManager configManager, string recompId)
    {
        string dataDirectory = FindDataDirectory(configManager);
        string libraryFilePath = dataDirectory == null ? null : Path.Combine(dataDirectory, "library.json");

        if (libraryFilePath == null || !File.Exists(libraryFilePath))
        {
            return (false, "RecompLauncher was not found.");
        }

        if (IsRunning())
        {
            return (false, "Close RecompLauncher first.");
        }

        try
        {
            JsonNode libraryNode = JsonNode.Parse(File.ReadAllText(libraryFilePath));

            if (libraryNode?["installed"] is not JsonObject installedNode || installedNode[recompId] is not JsonObject recompNode)
            {
                return (false, "It is no longer installed.");
            }

            string recompFolder = recompNode["path"]?.GetValue<string>();
            var gamesFolders = new List<string> { libraryNode["settings"]?["library_dir"]?.GetValue<string>() };

            if (libraryNode["settings"]?["library_dirs"] is JsonArray moreGamesFolders)
            {
                gamesFolders.AddRange(moreGamesFolders.Select(folderNode => folderNode?.GetValue<string>()));
            }

            bool isInsideGamesFolder = !string.IsNullOrEmpty(recompFolder) && gamesFolders.Any(gamesFolder =>
                !string.IsNullOrEmpty(gamesFolder)
                && NormaliseFolder(Path.GetFullPath(recompFolder)).StartsWith(NormaliseFolder(Path.GetFullPath(gamesFolder)) + "/", StringComparison.OrdinalIgnoreCase));

            if (!isInsideGamesFolder)
            {
                return (false, "It is installed outside RecompLauncher's games folder. Remove it in RecompLauncher.");
            }

            if (Directory.Exists(recompFolder))
            {
                Directory.Delete(recompFolder, true);
            }

            installedNode.Remove(recompId);

            string temporaryFilePath = libraryFilePath + ".rombox-tmp";
            File.WriteAllText(temporaryFilePath, libraryNode.ToJsonString());
            File.Copy(libraryFilePath, libraryFilePath + ".rombox-backup", true);
            File.Move(temporaryFilePath, libraryFilePath, true);
            cachedLibraryStamp = null;
            Godot.GD.Print($"[Recomps] removed {recompId} and its folder {recompFolder}.");
            return (true, null);
        }

        catch (Exception exception)
        {
            return (false, exception.Message);
        }
    }

    private static ProcessStartInfo BuildStartInfo(ConfigManager configManager, params string[] arguments)
    {
        string executablePath = FindExecutable(configManager);

        if (executablePath == null)
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath),
            UseShellExecute = false
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // The launcher's window is a web view, which keeps its own data in the user's profile unless told otherwise.
        if (IsPortable(executablePath))
        {
            startInfo.Environment["WEBVIEW2_USER_DATA_FOLDER"] = Path.Combine(Path.GetDirectoryName(executablePath), "data", "webview");
        }

        return startInfo;
    }

    // Adds folders to the list RecompLauncher scans for the player's own games. The launcher
    // rewrites its library file while it runs, so this only happens while it is closed.
    public static int AddRomFolders(ConfigManager configManager, IEnumerable<string> folderPaths)
    {
        string dataDirectory = FindDataDirectory(configManager);
        string libraryFilePath = dataDirectory == null ? null : Path.Combine(dataDirectory, "library.json");

        if (libraryFilePath == null || !File.Exists(libraryFilePath) || IsRunning())
        {
            return 0;
        }

        try
        {
            JsonNode libraryNode = JsonNode.Parse(File.ReadAllText(libraryFilePath));

            if (libraryNode?["settings"] is not JsonObject settingsNode)
            {
                return 0;
            }

            if (settingsNode["rom_dirs"] is not JsonArray romFoldersNode)
            {
                romFoldersNode = new JsonArray();
                settingsNode["rom_dirs"] = romFoldersNode;
            }

            bool movedGamesFolder = KeepGamesFolderInsideInstall(configManager, libraryNode, settingsNode);
            var knownFolders = romFoldersNode.Select(folderNode => NormaliseFolder(folderNode?.GetValue<string>())).ToHashSet(StringComparer.OrdinalIgnoreCase);
            int addedFolderCount = 0;

            foreach (string folderPath in folderPaths)
            {
                if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath) || !knownFolders.Add(NormaliseFolder(folderPath)))
                {
                    continue;
                }

                romFoldersNode.Add(Path.GetFullPath(folderPath).TrimEnd('/', '\\'));
                addedFolderCount++;
            }

            bool wroteGameFiles = WriteGameFiles(libraryNode, folderPaths);

            if (addedFolderCount == 0 && !movedGamesFolder && !wroteGameFiles)
            {
                return 0;
            }

            string temporaryFilePath = libraryFilePath + ".rombox-tmp";
            File.WriteAllText(temporaryFilePath, libraryNode.ToJsonString());
            File.Copy(libraryFilePath, libraryFilePath + ".rombox-backup", true);
            File.Move(temporaryFilePath, libraryFilePath, true);
            Godot.GD.Print($"[Recomps] added {addedFolderCount} game folder(s) to RecompLauncher's scan list.");
            return addedFolderCount;
        }

        catch (Exception exception)
        {
            Godot.GD.PrintErr($"[Recomps] could not add game folders to RecompLauncher: {exception.Message}");
            return 0;
        }
    }

    // A portable copy installs recomps into a games folder beside itself. The launcher's own default
    // is a folder at the top of the drive. Left alone once any recomp is installed.
    private static bool KeepGamesFolderInsideInstall(ConfigManager configManager, JsonNode libraryNode, JsonObject settingsNode)
    {
        string executablePath = FindExecutable(configManager);

        if (executablePath == null || !IsPortable(executablePath) || (libraryNode["installed"] is JsonObject installedNode && installedNode.Count > 0))
        {
            return false;
        }

        string gamesFolder = Path.Combine(Path.GetDirectoryName(executablePath), "games");
        string currentFolder = settingsNode["library_dir"]?.GetValue<string>();

        if (string.Equals(NormaliseFolder(currentFolder), NormaliseFolder(gamesFolder), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Directory.CreateDirectory(gamesFolder);
        settingsNode["library_dir"] = gamesFolder;
        settingsNode["asked_location"] = true;
        Godot.GD.Print($"[Recomps] RecompLauncher now installs recomps into {gamesFolder}.");
        return true;
    }

    private static readonly HashSet<string> GameFileExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".z64", ".n64", ".v64",
        ".iso", ".gcm", ".rvz", ".wia", ".wbfs", ".ciso", ".chd",
        ".nes", ".fds", ".unf", ".gb", ".gbc", ".gba", ".nds", ".3ds", ".cci", ".cia", ".cxi", ".vb", ".vboy",
        ".md", ".gen", ".smd", ".32x", ".gg", ".sms", ".sfc", ".smc", ".xci", ".nsp"
    };

    private const long SmallestGameFileBytes = 8 * 1024;

    // RecompLauncher only looks through its game folders when asked to in its own window, and it
    // offers the copies from its last look. RomBox writes that list for its own folders, in the
    // launcher's format: each game file with its console, title, region and size. RomBox knows
    // the console from the system folder a file sits in. Disc sets (.cue, .gdi, .m3u) and
    // archives are left for the launcher's own scan.
    private static bool WriteGameFiles(JsonNode libraryNode, IEnumerable<string> folderPaths)
    {
        var romboxFolders = folderPaths
            .Where(folderPath => !string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            .Select(folderPath => Path.GetFullPath(folderPath).TrimEnd('/', '\\'))
            .ToList();

        var gameFilesNode = new JsonArray();

        if (libraryNode["roms"] is JsonArray existingGameFilesNode)
        {
            foreach (JsonNode existingGameFile in existingGameFilesNode)
            {
                string existingPath = existingGameFile?["path"]?.GetValue<string>() ?? "";

                if (!romboxFolders.Any(romboxFolder => NormaliseFolder(existingPath).StartsWith(NormaliseFolder(romboxFolder) + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    gameFilesNode.Add(existingGameFile.DeepClone());
                }
            }
        }

        foreach (string romboxFolder in romboxFolders)
        {
            foreach (string systemFolder in Directory.GetDirectories(romboxFolder))
            {
                string origin = RecompLibrary.OriginForSystemSlug(Path.GetFileName(systemFolder));

                if (origin == null)
                {
                    continue;
                }

                foreach (string gameFilePath in Directory.EnumerateFiles(systemFolder, "*", SearchOption.AllDirectories))
                {
                    long gameFileBytes = new FileInfo(gameFilePath).Length;

                    if (!GameFileExtensions.Contains(Path.GetExtension(gameFilePath)) || gameFileBytes < SmallestGameFileBytes)
                    {
                        continue;
                    }

                    string title = Path.GetFileNameWithoutExtension(gameFilePath);
                    string region = "";

                    if (origin == "n64" && TryReadN64Header(gameFilePath, out string headerTitle, out string headerRegion))
                    {
                        title = headerTitle;
                        region = headerRegion;
                    }

                    gameFilesNode.Add(new JsonObject
                    {
                        ["path"] = gameFilePath,
                        ["system"] = origin,
                        ["title"] = title,
                        ["region"] = region,
                        ["size"] = gameFileBytes
                    });
                }
            }
        }

        if (libraryNode["roms"]?.ToJsonString() == gameFilesNode.ToJsonString())
        {
            return false;
        }

        libraryNode["roms"] = gameFilesNode;
        Godot.GD.Print($"[Recomps] told RecompLauncher about {gameFilesNode.Count} game file(s).");
        return true;
    }

    // An N64 dump carries the game's name at 0x20 and its region at 0x3E, in one of three byte orders.
    private static bool TryReadN64Header(string gameFilePath, out string title, out string region)
    {
        title = null;
        region = "";

        try
        {
            byte[] header = new byte[64];

            using (FileStream gameFile = File.OpenRead(gameFilePath))
            {
                if (gameFile.Read(header, 0, header.Length) != header.Length)
                {
                    return false;
                }
            }

            if (header[0] == 0x37 && header[1] == 0x80)
            {
                for (int offset = 0; offset < header.Length; offset += 2)
                {
                    (header[offset], header[offset + 1]) = (header[offset + 1], header[offset]);
                }
            }

            else if (header[0] == 0x40 && header[1] == 0x12)
            {
                for (int offset = 0; offset < header.Length; offset += 4)
                {
                    Array.Reverse(header, offset, 4);
                }
            }

            if (header[0] != 0x80 || header[1] != 0x37 || header[2] != 0x12 || header[3] != 0x40)
            {
                return false;
            }

            title = new string(header.Skip(0x20).Take(0x14).Select(value => value > 0x20 && value < 0x7F ? (char)value : ' ').ToArray()).Trim();

            region = (char)header[0x3E] switch
            {
                'E' or 'N' => "US",
                'P' or 'D' or 'F' or 'I' or 'S' or 'U' or 'X' or 'Y' => "Europe",
                'J' => "Japan",
                _ => ""
            };

            return title.Length > 0;
        }

        catch
        {
            return false;
        }
    }

    private static string NormaliseFolder(string folderPath)
    {
        return (folderPath ?? "").Replace('\\', '/').TrimEnd('/');
    }
}
