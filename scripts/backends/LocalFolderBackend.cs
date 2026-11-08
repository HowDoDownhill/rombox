using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

// Games already on disk: a root folder with one subfolder per system, as ES-DE, EmuDeck and
// RetroArch lay them out. The folder is scanned at every start, so nothing is cached.
public sealed class LocalFolderBackend : ILibraryBackend
{
    private LocalLibraryScan scan;

    public string SourceId => LibrarySources.Local;

    public string DisplayName => LibrarySources.DisplayName(LibrarySources.Local);

    public LibraryFeatures Features => LibraryFeatures.None;

    private string rootPath;
    private string biosPath;

    public Task<(bool isSuccess, string errorMessage)> ConnectAsync(ConfigManager configManager)
    {
        rootPath = configManager.LocalLibraryPath;
        biosPath = configManager.BiosPath;
        scan = null;

        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return Task.FromResult((false, "Choose the folder that holds your games."));
        }

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult((false, $"The folder {rootPath} wasn't found. If it's on a network share, check that the share is mounted."));
        }

        return Task.FromResult((true, (string)null));
    }

    public async Task<List<GameSystem>> GetSystemsAsync()
    {
        scan ??= await Task.Run(() => LocalLibraryScanner.Scan(rootPath));
        return scan.Systems;
    }

    public async Task<List<Game>> GetGamesAsync(GameSystem system, Action<int> reportCount = null)
    {
        scan ??= await Task.Run(() => LocalLibraryScanner.Scan(rootPath));
        List<Game> games = scan.Games.TryGetValue(system.Id, out var found) ? found : new List<Game>();

        foreach (Game game in games)
        {
            game.System = system;
        }

        reportCount?.Invoke(games.Count);
        return games;
    }

    public Task<List<Collection>> GetCollectionsAsync()
    {
        return Task.FromResult(new List<Collection>());
    }

    // Every file already in a system's BIOS folder counts as that system's firmware.
    public Task<List<Firmware>> GetFirmwareAsync()
    {
        var firmware = new List<Firmware>();

        if (scan == null || string.IsNullOrEmpty(biosPath))
        {
            return Task.FromResult(firmware);
        }

        foreach (GameSystem system in scan.Systems)
        {
            string systemBiosPath = Path.Combine(biosPath, system.Slug);

            if (!Directory.Exists(systemBiosPath))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(systemBiosPath))
            {
                string fileName = Path.GetFileName(file);

                if (fileName.EndsWith(".config.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                firmware.Add(new Firmware
                {
                    Id = LocalLibraryScanner.StableId("bios:" + system.Slug + "/" + fileName),
                    PlatformId = system.Id,
                    FileName = fileName,
                    FileNameNoExt = Path.GetFileNameWithoutExtension(fileName),
                    FileExtension = Path.GetExtension(fileName).TrimStart('.'),
                    FullPath = file,
                    FileSizeBytes = new FileInfo(file).Length
                });
            }
        }

        return Task.FromResult(firmware);
    }
}

public sealed class LocalLibraryScan
{
    public List<GameSystem> Systems { get; } = new List<GameSystem>();

    public Dictionary<int, List<Game>> Games { get; } = new Dictionary<int, List<Game>>();

    // Subfolders whose names match no known system.
    public List<string> UnrecognisedFolders { get; } = new List<string>();

    public int GameCount => Games.Values.Sum(list => list.Count);
}

public static class LocalLibraryScanner
{
    private const int MaximumFolderDepth = 3;

    // Folder names from ES-DE, EmuDeck and RetroArch, mapped to the slugs RomM and the emulator map use.
    private static readonly (string Slug, string Name, string[] Folders)[] KnownSystems =
    {
        ("nes", "Nintendo Entertainment System", new[] { "nes", "famicom", "fc" }),
        ("snes", "Super Nintendo Entertainment System", new[] { "snes", "sfc", "snesna", "superfamicom" }),
        ("n64", "Nintendo 64", new[] { "n64" }),
        ("gb", "Game Boy", new[] { "gb", "gameboy" }),
        ("gbc", "Game Boy Color", new[] { "gbc", "gameboycolor" }),
        ("gba", "Game Boy Advance", new[] { "gba", "gameboyadvance" }),
        ("nds", "Nintendo DS", new[] { "nds", "ds" }),
        ("nintendo-dsi", "Nintendo DSi", new[] { "dsi", "nintendo-dsi" }),
        ("new-nintendo-3ds", "Nintendo 3DS", new[] { "3ds", "n3ds", "new-nintendo-3ds" }),
        ("ngc", "Nintendo GameCube", new[] { "gc", "gamecube", "ngc" }),
        ("wii", "Wii", new[] { "wii" }),
        ("virtual-boy", "Virtual Boy", new[] { "virtualboy", "vb", "virtual-boy" }),
        ("psx", "PlayStation", new[] { "psx", "ps1", "playstation" }),
        ("ps2", "PlayStation 2", new[] { "ps2", "playstation2" }),
        ("ps3", "PlayStation 3", new[] { "ps3", "playstation3" }),
        ("psp", "PlayStation Portable", new[] { "psp" }),
        ("genesis", "Sega Mega Drive/Genesis", new[] { "genesis", "megadrive", "md" }),
        ("sms", "Sega Master System", new[] { "mastersystem", "sms" }),
        ("game-gear", "Sega Game Gear", new[] { "gamegear", "gg", "game-gear" }),
        ("segacd", "Sega CD", new[] { "segacd", "megacd" }),
        ("sega32", "Sega 32X", new[] { "sega32x", "32x", "sega32" }),
        ("saturn", "Sega Saturn", new[] { "saturn" }),
        ("dc", "Dreamcast", new[] { "dreamcast", "dc" }),
        ("sg-1000", "SG-1000", new[] { "sg-1000", "sg1000" }),
        ("pce", "TurboGrafx-16/PC Engine", new[] { "pcengine", "tg16", "turbografx16", "pce" }),
        ("pcfx", "PC-FX", new[] { "pcfx" }),
        ("arcade", "Arcade", new[] { "arcade", "mame", "fbneo", "fba" }),
        ("neogeomvs", "Neo Geo", new[] { "neogeo", "neogeomvs" }),
        ("neo-geo-pocket", "Neo Geo Pocket", new[] { "ngp", "neo-geo-pocket" }),
        ("neo-geo-pocket-color", "Neo Geo Pocket Color", new[] { "ngpc", "neo-geo-pocket-color" }),
        ("atari2600", "Atari 2600", new[] { "atari2600" }),
        ("atari5200", "Atari 5200", new[] { "atari5200" }),
        ("atari7800", "Atari 7800", new[] { "atari7800" }),
        ("lynx", "Atari Lynx", new[] { "atarilynx", "lynx" }),
        ("jaguar", "Atari Jaguar", new[] { "atarijaguar", "jaguar" }),
        ("wonderswan", "WonderSwan", new[] { "wonderswan", "ws" }),
        ("wonderswan-color", "WonderSwan Color", new[] { "wonderswancolor", "wsc", "wonderswan-color" }),
        ("3do", "3DO", new[] { "3do" }),
        ("colecovision", "ColecoVision", new[] { "colecovision", "coleco" }),
        ("intellivision", "Intellivision", new[] { "intellivision" }),
        ("msx", "MSX", new[] { "msx", "msx1" }),
        ("msx2", "MSX2", new[] { "msx2" }),
        ("c64", "Commodore 64", new[] { "c64" }),
        ("amiga", "Amiga", new[] { "amiga" }),
        ("vectrex", "Vectrex", new[] { "vectrex" })
    };

    // Files that sit beside games but are not games. .md stays out of this list: it is a Mega Drive ROM.
    private static readonly HashSet<string> IgnoredExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".nfo", ".pdf", ".htm", ".html", ".url",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".mp4", ".mkv", ".avi",
        ".xml", ".json", ".dat", ".ini", ".cfg", ".db", ".log", ".sav", ".srm", ".state", ".sha1", ".md5", ".sfv"
    };

    // Track files that a .ccd or .toc names only by sharing its base name.
    private static readonly HashSet<string> DiscTrackExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".bin", ".img", ".sub", ".raw"
    };

    private static readonly HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "media", "images", "videos", "manuals", "snap", "snaps", "screenshots", "covers", "boxart",
        "saves", "states", "savestates", "downloaded_media", "bios", "system"
    };

    private static readonly Regex TrailingTags = new Regex(@"(\s*[\(\[][^\)\]]*[\)\]])+\s*$", RegexOptions.Compiled);

    public static (string Slug, string Name)? MatchFolder(string folderName)
    {
        foreach (var system in KnownSystems)
        {
            if (system.Folders.Any(folder => string.Equals(folder, folderName, StringComparison.OrdinalIgnoreCase)))
            {
                return (system.Slug, system.Name);
            }
        }

        return null;
    }

    public static LocalLibraryScan Scan(string rootPath)
    {
        var result = new LocalLibraryScan();

        if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
        {
            return result;
        }

        var systemFolders = new List<(string Path, string Slug, string Name)>();
        var rootMatch = MatchFolder(Path.GetFileName(Path.TrimEndingDirectorySeparator(rootPath)));

        // The chosen folder can itself be a system folder, such as .../roms/snes.
        if (rootMatch.HasValue)
        {
            systemFolders.Add((rootPath, rootMatch.Value.Slug, rootMatch.Value.Name));
        }
        else
        {
            foreach (string folder in SafeDirectories(rootPath).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string folderName = Path.GetFileName(folder);
                var match = MatchFolder(folderName);

                if (match.HasValue)
                {
                    systemFolders.Add((folder, match.Value.Slug, match.Value.Name));
                }
                else if (!folderName.StartsWith(".") && !IgnoredFolders.Contains(folderName))
                {
                    result.UnrecognisedFolders.Add(folderName);
                }
            }
        }

        var usedGameIds = new HashSet<int>();

        // Two folders for one system, such as genesis and megadrive, merge into one system.
        foreach (var group in systemFolders.GroupBy(folder => folder.Slug))
        {
            var system = new GameSystem
            {
                Id = StableId("system:" + group.Key),
                Slug = group.Key,
                IgdbSlug = group.Key,
                Name = group.First().Name
            };

            var games = new List<Game>();

            foreach (var folder in group)
            {
                CollectGames(folder.Path, rootPath, system, games, usedGameIds, 0);
            }

            if (games.Count == 0)
            {
                continue;
            }

            DisambiguateNames(games);
            games.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            system.RomCount = games.Count;
            result.Systems.Add(system);
            result.Games[system.Id] = games;
        }

        result.Systems.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static void CollectGames(string folder, string rootPath, GameSystem system, List<Game> games, HashSet<int> usedGameIds, int depth)
    {
        string[] files = SafeFiles(folder);
        var skipped = FilesCoveredByDiscIndexes(folder, files);

        foreach (string file in files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(file);
            string extension = Path.GetExtension(file);

            if (fileName.StartsWith(".") || string.IsNullOrEmpty(extension) || IgnoredExtensions.Contains(extension) || skipped.Contains(fileName))
            {
                continue;
            }

            string relativePath = Path.GetRelativePath(rootPath, file);
            int id = StableId(relativePath.Replace('\\', '/').ToLowerInvariant());

            while (!usedGameIds.Add(id))
            {
                id = id == int.MaxValue ? 1 : id + 1;
            }

            long size = 0;

            try
            {
                size = new FileInfo(file).Length;
            }
            catch (IOException)
            {
            }

            games.Add(new Game
            {
                Id = id,
                Name = CleanName(Path.GetFileNameWithoutExtension(fileName)),
                LocalFilename = fileName,
                LocalPath = Path.GetFullPath(file),
                FileSizeBytes = size,
                PlatformId = system.Id,
                PlatformSlug = system.Slug,
                PlatformDisplayName = system.Name,
                Files = new List<RomFile> { new RomFile { Id = id, FileName = fileName, FullPath = Path.GetFullPath(file) } },
                System = system
            });
        }

        if (depth >= MaximumFolderDepth)
        {
            return;
        }

        foreach (string subfolder in SafeDirectories(folder))
        {
            string name = Path.GetFileName(subfolder);

            if (!name.StartsWith(".") && !IgnoredFolders.Contains(name))
            {
                CollectGames(subfolder, rootPath, system, games, usedGameIds, depth + 1);
            }
        }
    }

    // A disc lists once, through its .cue, .gdi, .ccd or .toc; the track files those point at are
    // not separate games. Discs listed in an .m3u play through the playlist. A .bin that nothing
    // points at stays, since Genesis and Atari ROMs often use that extension.
    private static HashSet<string> FilesCoveredByDiscIndexes(string folder, string[] files)
    {
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in files)
        {
            string extension = Path.GetExtension(file).ToLowerInvariant();

            switch (extension)
            {
                case ".cue":
                    foreach (string line in SafeLines(file))
                    {
                        string track = MatchedFileName(CueFileLine.Match(line));

                        if (track != null)
                        {
                            covered.Add(track);
                        }
                    }
                    break;

                case ".gdi":
                    foreach (string line in SafeLines(file).Skip(1))
                    {
                        string track = MatchedFileName(GdiTrackLine.Match(line));

                        if (track != null)
                        {
                            covered.Add(track);
                        }
                    }
                    break;

                case ".ccd":
                case ".toc":
                    string baseName = Path.GetFileNameWithoutExtension(file);

                    foreach (string trackExtension in DiscTrackExtensions)
                    {
                        covered.Add(baseName + trackExtension);
                    }
                    break;

                case ".m3u":
                    foreach (string line in SafeLines(file))
                    {
                        string entry = line.Trim();

                        if (entry.Length > 0 && !entry.StartsWith("#"))
                        {
                            covered.Add(Path.GetFileName(entry.Replace('\\', '/')));
                        }
                    }
                    break;
            }
        }

        return covered;
    }

    private static string MatchedFileName(Match match)
    {
        if (!match.Success)
        {
            return null;
        }

        string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        return Path.GetFileName(value.Replace('\\', '/'));
    }

    private static readonly Regex CueFileLine = new Regex("^\\s*FILE\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // "1 0 4 2352 track01.bin 0", where the file name may be quoted.
    private static readonly Regex GdiTrackLine = new Regex("^\\s*\\d+\\s+\\d+\\s+\\d+\\s+\\d+\\s+(?:\"([^\"]+)\"|(\\S+))", RegexOptions.Compiled);

    private static string CleanName(string fileNameWithoutExtension)
    {
        string cleaned = TrailingTags.Replace(fileNameWithoutExtension, "").Trim();
        return cleaned.Length > 0 ? cleaned : fileNameWithoutExtension;
    }

    // Region and revision tags come off the names. Where that leaves two games with one name,
    // both keep their full file names.
    private static void DisambiguateNames(List<Game> games)
    {
        foreach (var duplicates in games.GroupBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
        {
            foreach (Game game in duplicates)
            {
                game.Name = Path.GetFileNameWithoutExtension(game.LocalFilename);
            }
        }
    }

    // FNV-1a, kept positive and non-zero, so a game keeps its id across scans.
    public static int StableId(string key)
    {
        uint hash = 2166136261;

        foreach (char character in key)
        {
            hash ^= character;
            hash *= 16777619;
        }

        int id = (int)(hash & 0x7FFFFFFF);
        return id == 0 ? 1 : id;
    }

    private static string[] SafeFiles(string folder)
    {
        try
        {
            return Directory.GetFiles(folder);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            GD.PrintErr($"[Library] couldn't read {folder}: {exception.Message}");
            return Array.Empty<string>();
        }
    }

    private static string[] SafeDirectories(string folder)
    {
        try
        {
            return Directory.GetDirectories(folder);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            GD.PrintErr($"[Library] couldn't read {folder}: {exception.Message}");
            return Array.Empty<string>();
        }
    }

    private static string[] SafeLines(string file)
    {
        try
        {
            return File.ReadAllLines(file);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
