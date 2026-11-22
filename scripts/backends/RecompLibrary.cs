using System;
using System.Collections.Generic;
using System.Linq;

// Puts the recomps installed through RecompLauncher into the game list of the console each one
// came from. They are added after the library loads and are never written to the game cache.
public static class RecompLibrary
{
    // RecompLauncher's console ids, and the system slugs a library may use for the same console.
    private static readonly Dictionary<string, string[]> SystemSlugsByOrigin = new Dictionary<string, string[]>
    {
        { "n64", new[] { "n64" } },
        { "x360", new[] { "xbox360", "x360" } },
        { "xbox", new[] { "xbox" } },
        { "xseries", new[] { "series-x-s", "xboxseries" } },
        { "ps1", new[] { "psx", "ps", "ps1" } },
        { "ps2", new[] { "ps2" } },
        { "ps3", new[] { "ps3" } },
        { "ps4", new[] { "ps4", "ps4--1" } },
        { "ps5", new[] { "ps5" } },
        { "psp", new[] { "psp" } },
        { "gc", new[] { "ngc", "gc", "gamecube" } },
        { "wii", new[] { "wii" } },
        { "wiiu", new[] { "wiiu" } },
        { "switch", new[] { "switch" } },
        { "switch2", new[] { "switch-2", "switch2" } },
        { "nes", new[] { "nes", "famicom" } },
        { "snes", new[] { "snes", "sfam" } },
        { "gb", new[] { "gb" } },
        { "gbc", new[] { "gbc" } },
        { "gba", new[] { "gba" } },
        { "ds", new[] { "nds", "ds" } },
        { "3ds", new[] { "3ds", "n3ds" } },
        { "vb", new[] { "virtualboy", "vb" } },
        { "dc", new[] { "dc", "dreamcast" } },
        { "saturn", new[] { "saturn" } },
        { "genesis", new[] { "genesis", "genesis-slash-megadrive", "megadrive" } },
        { "s32x", new[] { "sega32", "sega32x" } },
        { "gg", new[] { "gamegear", "gg" } },
        { "arcade", new[] { "arcade" } },
        { "cdi", new[] { "philips-cd-i", "cdi" } },
        { "amiga", new[] { "amiga" } },
        { "pc", new[] { "win", "pc", "dos" } }
    };

    public static string OriginForSystemSlug(string systemSlug)
    {
        return SystemSlugsByOrigin.FirstOrDefault(entry => entry.Value.Contains(systemSlug, StringComparer.OrdinalIgnoreCase)).Key;
    }

    // Returns true when the set of recomps in the lists changed.
    public static bool Merge(AppInstance appInstance)
    {
        DataBus dataBus = appInstance?.dataBus;

        if (dataBus?.gameCache == null || dataBus.systems == null)
        {
            return false;
        }

        var previousRecompIds = dataBus.gameCache.Values
            .SelectMany(games => games)
            .Where(game => game.IsRecomp)
            .Select(game => game.RecompId)
            .ToHashSet();

        foreach (List<Game> games in dataBus.gameCache.Values)
        {
            games.RemoveAll(game => game.IsRecomp);
        }

        var currentRecompIds = new HashSet<string>();

        if (AndroidApps.IsAvailable)
        {
            return previousRecompIds.Count > 0;
        }

        foreach (InstalledRecomp recomp in RecompLauncher.ReadInstalledRecomps(appInstance.configManager))
        {
            GameSystem system = FindSystem(dataBus.systems, recomp.Origin);

            if (system == null)
            {
                Godot.GD.Print($"[Recomps] {recomp.ProjectName} is installed, and this library has no system for '{recomp.Origin}' to list it under.");
                continue;
            }

            if (!dataBus.gameCache.TryGetValue(system.Id, out List<Game> systemGames))
            {
                systemGames = new List<Game>();
                dataBus.gameCache[system.Id] = systemGames;
            }

            Game recompGame = BuildGame(recomp, system);
            int insertIndex = systemGames.FindIndex(existing => string.Compare(existing.Name, recompGame.Name, StringComparison.OrdinalIgnoreCase) > 0);
            systemGames.Insert(insertIndex < 0 ? systemGames.Count : insertIndex, recompGame);
            currentRecompIds.Add(recomp.Id);
        }

        MarkGamesWithAvailableRecomps(appInstance, currentRecompIds);
        return !previousRecompIds.SetEquals(currentRecompIds);
    }

    private static readonly Dictionary<string, string> matchNamesByGameName = new Dictionary<string, string>();

    // Lower case, no accents, no tags in brackets, no punctuation, and a trailing ", The" moved
    // to the front, so "Legend of Zelda, The - Majora's Mask (USA)" matches the catalog's name.
    public static string BuildMatchName(string gameName)
    {
        if (string.IsNullOrEmpty(gameName))
        {
            return "";
        }

        if (matchNamesByGameName.TryGetValue(gameName, out string cachedMatchName))
        {
            return cachedMatchName;
        }

        string withoutTags = System.Text.RegularExpressions.Regex.Replace(gameName, @"\s*[\(\[][^\)\]]*[\)\]]", "");
        withoutTags = System.Text.RegularExpressions.Regex.Replace(withoutTags, @"^(.*?), (The|A|An)\b", "$2 $1", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        var matchName = new System.Text.StringBuilder();

        foreach (char character in withoutTags.Normalize(System.Text.NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(character))
            {
                matchName.Append(char.ToLowerInvariant(character));
            }

            else if (character == '&')
            {
                matchName.Append("and");
            }
        }

        string result = matchName.ToString();

        if (result.StartsWith("the"))
        {
            result = result.Substring(3);
        }

        matchNamesByGameName[gameName] = result;
        return result;
    }

    // Marks each game that has a recomp in RecompLauncher's catalog which is not installed yet.
    private static void MarkGamesWithAvailableRecomps(AppInstance appInstance, HashSet<string> installedRecompIds)
    {
        DataBus dataBus = appInstance.dataBus;
        var catalogByOrigin = RecompLauncher.ReadCatalog(appInstance.configManager)
            .Where(recomp => !installedRecompIds.Contains(recomp.Id))
            .GroupBy(recomp => recomp.Origin)
            .ToDictionary(sameOrigin => sameOrigin.Key, sameOrigin => sameOrigin.ToLookup(recomp => BuildMatchName(recomp.GameName)));

        int markedGameCount = 0;

        foreach (GameSystem system in dataBus.systems)
        {
            if (!dataBus.gameCache.TryGetValue(system.Id, out List<Game> systemGames))
            {
                continue;
            }

            string origin = SystemSlugsByOrigin.FirstOrDefault(entry => entry.Value.Contains(system.Slug, StringComparer.OrdinalIgnoreCase)).Key;
            ILookup<string, CatalogRecomp> recompsByMatchName = null;

            if (origin != null)
            {
                catalogByOrigin.TryGetValue(origin, out recompsByMatchName);
            }

            foreach (Game game in systemGames)
            {
                game.AvailableRecomps = null;

                if (recompsByMatchName == null || game.IsRecomp)
                {
                    continue;
                }

                string matchName = BuildMatchName(game.Name);

                if (matchName.Length > 0 && recompsByMatchName.Contains(matchName))
                {
                    game.AvailableRecomps = recompsByMatchName[matchName].ToList();
                    markedGameCount++;
                }
            }
        }

        if (markedGameCount != lastMarkedGameCount)
        {
            lastMarkedGameCount = markedGameCount;
            Godot.GD.Print($"[Recomps] {markedGameCount} game(s) in this library have a recomp that is not installed yet.");
        }
    }

    private static int lastMarkedGameCount = -1;

    private static GameSystem FindSystem(List<GameSystem> systems, string origin)
    {
        if (string.IsNullOrEmpty(origin) || !SystemSlugsByOrigin.TryGetValue(origin, out string[] systemSlugs))
        {
            return null;
        }

        return systems.FirstOrDefault(system => systemSlugs.Contains(system.Slug, StringComparer.OrdinalIgnoreCase));
    }

    private static Game BuildGame(InstalledRecomp recomp, GameSystem system)
    {
        return new Game
        {
            Id = LocalLibraryScanner.StableId("recomp:" + recomp.Id),
            Name = recomp.GameName ?? recomp.ProjectName,
            Description = recomp.Summary,
            PathCoverLarge = recomp.CoverUrl,
            PlatformId = system.Id,
            PlatformSlug = system.Slug,
            PlatformDisplayName = system.Name,
            LocalCompany = recomp.Author,
            LocalYear = recomp.ReleaseYear,
            System = system,
            RecompId = recomp.Id,
            RecompProjectName = recomp.ProjectName,
            RecompLogoUrl = recomp.LogoUrl
        };
    }
}
