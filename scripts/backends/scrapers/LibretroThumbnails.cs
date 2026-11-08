using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

// Box art, logos, title screens and screenshots from RetroArch's thumbnail server. It needs no
// account. Each system's box art listing is fetched once and kept on disk, so matching a game costs
// no request and a game with no art never touches the server.
public sealed class LibretroThumbnails : IArtworkScraper
{
    private const string ServerUrl = "https://thumbnails.libretro.com/";
    private const string BoxArtFolder = "Named_Boxarts";
    private const string IndexFolderName = "libretro";

    private static readonly TimeSpan IndexMaximumAge = TimeSpan.FromDays(30);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);
    private static readonly Regex ListingEntry = new Regex("href=\"([^\"?/][^\"]*\\.png)\"", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // This client carries no credentials. RomMAPI's would send the user's server login to this host.
    private static readonly HttpClient Client = CreateClient();

    private static readonly Dictionary<string, string> FolderBySlug = new Dictionary<string, string>
    {
        { "nes", "Nintendo - Nintendo Entertainment System" },
        { "snes", "Nintendo - Super Nintendo Entertainment System" },
        { "n64", "Nintendo - Nintendo 64" },
        { "gb", "Nintendo - Game Boy" },
        { "gbc", "Nintendo - Game Boy Color" },
        { "gba", "Nintendo - Game Boy Advance" },
        { "nds", "Nintendo - Nintendo DS" },
        { "nintendo-dsi", "Nintendo - Nintendo DSi" },
        { "new-nintendo-3ds", "Nintendo - Nintendo 3DS" },
        { "ngc", "Nintendo - GameCube" },
        { "wii", "Nintendo - Wii" },
        { "virtual-boy", "Nintendo - Virtual Boy" },
        { "psx", "Sony - PlayStation" },
        { "ps2", "Sony - PlayStation 2" },
        { "ps3", "Sony - PlayStation 3" },
        { "psp", "Sony - PlayStation Portable" },
        { "genesis", "Sega - Mega Drive - Genesis" },
        { "sms", "Sega - Master System - Mark III" },
        { "game-gear", "Sega - Game Gear" },
        { "segacd", "Sega - Mega-CD - Sega CD" },
        { "sega32", "Sega - 32X" },
        { "saturn", "Sega - Saturn" },
        { "dc", "Sega - Dreamcast" },
        { "sg-1000", "Sega - SG-1000" },
        { "pce", "NEC - PC Engine - TurboGrafx 16" },
        { "pcfx", "NEC - PC-FX" },
        { "arcade", "MAME" },
        { "neogeomvs", "SNK - Neo Geo" },
        { "neo-geo-pocket", "SNK - Neo Geo Pocket" },
        { "neo-geo-pocket-color", "SNK - Neo Geo Pocket Color" },
        { "atari2600", "Atari - 2600" },
        { "atari5200", "Atari - 5200" },
        { "atari7800", "Atari - 7800" },
        { "lynx", "Atari - Lynx" },
        { "jaguar", "Atari - Jaguar" },
        { "wonderswan", "Bandai - WonderSwan" },
        { "wonderswan-color", "Bandai - WonderSwan Color" },
        { "3do", "The 3DO Company - 3DO" },
        { "colecovision", "Coleco - ColecoVision" },
        { "intellivision", "Mattel - Intellivision" },
        { "msx", "Microsoft - MSX" },
        { "msx2", "Microsoft - MSX2" },
        { "c64", "Commodore - 64" },
        { "amiga", "Commodore - Amiga" },
        { "vectrex", "GCE - Vectrex" }
    };

    private readonly ConcurrentDictionary<string, Lazy<Task<LibretroThumbnailMatcher.Index>>> indexes = new();
    private readonly ConcurrentDictionary<string, DateTime> failedAt = new();

    public string DisplayName => "libretro thumbnails";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(HttpHandlers.Create()) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RomBox/1.0");
        return client;
    }

    public async Task<List<ScrapedAsset>> FindAsync(Game game, string assetsPath)
    {
        var assets = new List<ScrapedAsset>();
        string slug = game.System?.Slug ?? game.PlatformSlug;

        if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(game.LocalFilename) || !FolderBySlug.TryGetValue(slug, out string folder))
        {
            return assets;
        }

        LibretroThumbnailMatcher.Index index = await GetIndexAsync(slug, folder, assetsPath);

        if (index == null)
        {
            return assets;
        }

        string match = LibretroThumbnailMatcher.Find(index, Path.GetFileNameWithoutExtension(game.LocalFilename));

        if (match == null)
        {
            return assets;
        }

        // The other art folders use the box art's name, and a game missing from one just returns 404.
        assets.Add(new ScrapedAsset("box2d", ArtUrl(folder, BoxArtFolder, match), Path.Combine(assetsPath, "covers_2d", $"{game.Id}.png")));
        assets.Add(new ScrapedAsset("marquee", ArtUrl(folder, "Named_Logos", match), Path.Combine(assetsPath, "marquees", $"{game.Id}.png")));
        assets.Add(new ScrapedAsset("screenshot", ArtUrl(folder, "Named_Titles", match), Path.Combine(assetsPath, "screenshots", $"{game.Id}_0.jpg")));
        assets.Add(new ScrapedAsset("screenshot", ArtUrl(folder, "Named_Snaps", match), Path.Combine(assetsPath, "screenshots", $"{game.Id}_1.jpg")));
        assets.RemoveAll(asset => File.Exists(asset.LocalFilePath));
        return assets;
    }

    private static string ArtUrl(string folder, string artFolder, string name)
    {
        return $"{ServerUrl}{Uri.EscapeDataString(folder)}/{artFolder}/{Uri.EscapeDataString(name + ".png")}";
    }

    private Task<LibretroThumbnailMatcher.Index> GetIndexAsync(string slug, string folder, string assetsPath)
    {
        if (failedAt.TryGetValue(slug, out DateTime lastFailure) && DateTime.UtcNow - lastFailure < RetryAfterFailure)
        {
            return Task.FromResult<LibretroThumbnailMatcher.Index>(null);
        }

        // Lazy, so a screen of cards appearing at once fetches the listing a single time.
        Lazy<Task<LibretroThumbnailMatcher.Index>> entry = indexes.GetOrAdd(
            slug,
            _ => new Lazy<Task<LibretroThumbnailMatcher.Index>>(() => Task.Run(() => LoadIndexAsync(slug, folder, assetsPath))));

        return AwaitIndex(slug, entry);
    }

    private async Task<LibretroThumbnailMatcher.Index> AwaitIndex(string slug, Lazy<Task<LibretroThumbnailMatcher.Index>> entry)
    {
        LibretroThumbnailMatcher.Index index = await entry.Value;

        if (index == null)
        {
            failedAt[slug] = DateTime.UtcNow;
            indexes.TryRemove(slug, out _);
        }

        return index;
    }

    private static async Task<LibretroThumbnailMatcher.Index> LoadIndexAsync(string slug, string folder, string assetsPath)
    {
        string indexPath = Path.Combine(assetsPath, IndexFolderName, slug + ".txt");
        bool haveSavedIndex = File.Exists(indexPath);

        if (haveSavedIndex && DateTime.UtcNow - File.GetLastWriteTimeUtc(indexPath) < IndexMaximumAge)
        {
            return LibretroThumbnailMatcher.BuildIndex(File.ReadAllLines(indexPath));
        }

        try
        {
            string listing = await Client.GetStringAsync($"{ServerUrl}{Uri.EscapeDataString(folder)}/{BoxArtFolder}/");
            List<string> names = ListingEntry.Matches(listing)
                .Select(match => Uri.UnescapeDataString(WebUtility.HtmlDecode(match.Groups[1].Value)))
                .Select(file => file.Substring(0, file.Length - ".png".Length))
                .ToList();

            if (names.Count > 0)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(indexPath));
                File.WriteAllLines(indexPath, names);
                GD.Print($"[Scraper] listed {names.Count} {slug} thumbnails.");
                return LibretroThumbnailMatcher.BuildIndex(names);
            }

            GD.PrintErr($"[Scraper] the {slug} thumbnail listing had no entries.");
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is IOException)
        {
            GD.PrintErr($"[Scraper] couldn't list {slug} thumbnails: {exception.Message}");
        }

        // An old listing beats none when the server can't be reached.
        return haveSavedIndex ? LibretroThumbnailMatcher.BuildIndex(File.ReadAllLines(indexPath)) : null;
    }

    public async Task<bool> DownloadAsync(string url, string destinationFilePath)
    {
        string partialPath = $"{destinationFilePath}.{Guid.NewGuid():N}.part";

        try
        {
            using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode != HttpStatusCode.NotFound)
                {
                    GD.PrintErr($"[Scraper] {url} returned {(int)response.StatusCode}.");
                }

                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath));

            using (var source = await response.Content.ReadAsStreamAsync())
            using (var destination = new FileStream(partialPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None))
            {
                await source.CopyToAsync(destination);
            }

            File.Move(partialPath, destinationFilePath, true);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is IOException)
        {
            GD.PrintErr($"[Scraper] couldn't download {url}: {exception.Message}");

            try
            {
                File.Delete(partialPath);
            }
            catch (IOException)
            {
            }

            return false;
        }
    }
}
