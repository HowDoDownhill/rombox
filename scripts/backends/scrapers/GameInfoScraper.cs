using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public class LocalGameInfo
{
    [JsonPropertyName("size")]
    public long FileSizeBytes { get; set; }

    [JsonPropertyName("checked_unix")]
    public long CheckedUnix { get; set; }

    [JsonPropertyName("found")]
    public bool Found { get; set; }

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; }

    [JsonPropertyName("year")]
    public string Year { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; }
}

// Identifies a local game by its file hash through Hasheous, for the publisher, year and description.
// The hash leaves this machine only for the game the player stays on, one lookup at a time.
public sealed class GameInfoScraper
{
    private const string LookupUrl = "https://hasheous.org/api/v1/Lookup/ByHash/";
    private const string InfoFileName = "game_info.json";
    private const string AiDescriptionAttribute = "AIDescription";
    private const string AiDescriptionNote = "AI-written summary from Hasheous.";
    private const long MaximumHashedBytes = 1024L * 1024L * 1024L;
    private const int SettleMilliseconds = 700;
    private const int LookupGapMilliseconds = 1000;

    private static readonly TimeSpan RetryNotFoundAfter = TimeSpan.FromDays(30);
    private static readonly TimeSpan DefaultBackoff = TimeSpan.FromMinutes(5);

    // These hold a compressed disc or a custom container, so their hash matches no known dump.
    private static readonly HashSet<string> UnidentifiableExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".7z", ".rar", ".chd", ".rvz", ".wbfs", ".cso", ".pbp", ".ccd", ".toc"
    };

    private static readonly HttpClient Client = CreateClient();

    private readonly AppInstance appInstance;
    private Dictionary<int, LocalGameInfo> infoByGameId;
    private Game pendingGame;
    private bool isRunning;
    private DateTime pausedUntil = DateTime.MinValue;

    public event Action<Game> Updated;

    public GameInfoScraper(AppInstance appInstance)
    {
        this.appInstance = appInstance;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(HttpHandlers.Create()) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RomBox/1.0");
        return client;
    }

    private string InfoFilePath => Path.Combine(appInstance.configManager.AssetsPath, InfoFileName);

    // Fills in a game the player selected. Saved answers apply at once; anything else is looked up
    // once the selection settles, so scrolling through a list hashes nothing.
    public void Request(Game game)
    {
        if (game == null || !appInstance.configManager.ScrapeGameInfo)
        {
            return;
        }

        LoadSavedInfo();

        if (infoByGameId.TryGetValue(game.Id, out LocalGameInfo saved) && saved.FileSizeBytes == game.FileSizeBytes)
        {
            Apply(game, saved);

            if (saved.Found || DateTimeOffset.UtcNow.ToUnixTimeSeconds() - saved.CheckedUnix < (long)RetryNotFoundAfter.TotalSeconds)
            {
                return;
            }
        }

        pendingGame = game;

        if (!isRunning)
        {
            RunLookups();
        }
    }

    private async void RunLookups()
    {
        isRunning = true;

        try
        {
            while (pendingGame != null)
            {
                Game game = pendingGame;
                await Task.Delay(SettleMilliseconds);

                if (pendingGame != game)
                {
                    continue;
                }

                pendingGame = null;

                if (DateTime.UtcNow < pausedUntil)
                {
                    continue;
                }

                await LookUp(game);
                await Task.Delay(LookupGapMilliseconds);
            }
        }
        finally
        {
            isRunning = false;
        }
    }

    private async Task LookUp(Game game)
    {
        string path = game.LocalPath;
        var fingerprint = await Task.Run(() => Fingerprint(path));

        if (fingerprint == null)
        {
            Save(game, new LocalGameInfo());
            return;
        }

        try
        {
            using HttpResponseMessage response = await Client.GetAsync($"{LookupUrl}{fingerprint.Value.Kind}/{fingerprint.Value.Hash}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                GD.Print($"[GameInfo] no match for {game.Name}.");
                Save(game, new LocalGameInfo());
                return;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                pausedUntil = DateTime.UtcNow + (response.Headers.RetryAfter?.Delta ?? DefaultBackoff);
                GD.Print($"[GameInfo] asked to slow down until {pausedUntil:HH:mm:ss} UTC.");
                return;
            }

            if (!response.IsSuccessStatusCode)
            {
                GD.PrintErr($"[GameInfo] lookup returned {(int)response.StatusCode}.");
                return;
            }

            LocalGameInfo info = Parse(await response.Content.ReadAsStringAsync());
            GD.Print($"[GameInfo] matched {game.Name}: {info.Publisher} {info.Year}.");
            Save(game, info);
            Apply(game, info);
            Updated?.Invoke(game);
        }
        catch (Exception exception) when (exception is HttpRequestException || exception is TaskCanceledException || exception is JsonException)
        {
            GD.PrintErr($"[GameInfo] lookup failed: {exception.Message}");
        }
    }

    private static LocalGameInfo Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        var info = new LocalGameInfo { Found = true };

        if (root.TryGetProperty("publisher", out JsonElement publisher) && publisher.ValueKind == JsonValueKind.Object)
        {
            info.Publisher = ReadString(publisher, "name");
        }

        if (root.TryGetProperty("signature", out JsonElement signature) && signature.ValueKind == JsonValueKind.Object
            && signature.TryGetProperty("game", out JsonElement game) && game.ValueKind == JsonValueKind.Object)
        {
            info.Year = ReadString(game, "year");
        }

        if (root.TryGetProperty("attributes", out JsonElement attributes) && attributes.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement attribute in attributes.EnumerateArray())
            {
                if (ReadString(attribute, "attributeName") == AiDescriptionAttribute)
                {
                    info.Description = ReadString(attribute, "value");
                }
            }
        }

        return info;
    }

    private static string ReadString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static void Apply(Game game, LocalGameInfo info)
    {
        if (!info.Found)
        {
            return;
        }

        string credits = string.Join(" - ", new[] { info.Publisher, info.Year }.Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new List<string>();

        if (credits.Length > 0)
        {
            parts.Add(credits);
        }

        if (!string.IsNullOrWhiteSpace(info.Description))
        {
            parts.Add(info.Description);
            parts.Add(AiDescriptionNote);
        }

        game.Description = string.Join("\n\n", parts);
    }

    private void LoadSavedInfo()
    {
        if (infoByGameId != null)
        {
            return;
        }

        infoByGameId = new Dictionary<int, LocalGameInfo>();

        try
        {
            if (File.Exists(InfoFilePath))
            {
                infoByGameId = JsonSerializer.Deserialize(File.ReadAllText(InfoFilePath), RommJsonContext.Default.DictionaryInt32LocalGameInfo) ?? infoByGameId;
            }
        }
        catch (Exception exception) when (exception is IOException || exception is JsonException)
        {
            GD.PrintErr($"[GameInfo] couldn't read saved game info: {exception.Message}");
        }
    }

    private void Save(Game game, LocalGameInfo info)
    {
        info.FileSizeBytes = game.FileSizeBytes;
        info.CheckedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        infoByGameId[game.Id] = info;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InfoFilePath));
            File.WriteAllText(InfoFilePath, JsonSerializer.Serialize(infoByGameId, RommJsonContext.Default.DictionaryInt32LocalGameInfo));
        }
        catch (IOException exception)
        {
            GD.PrintErr($"[GameInfo] couldn't save game info: {exception.Message}");
        }
    }

    // The hash that identifies a file. A zip's CRC comes from its directory, so nothing is unpacked.
    // A disc index is identified by its first track.
    private static (string Kind, string Hash)? Fingerprint(string path)
    {
        try
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension == ".m3u" || extension == ".cue" || extension == ".gdi")
            {
                string track = LocalLibraryScanner.FirstDiscFile(path);
                return track != null && File.Exists(track) ? Fingerprint(track) : null;
            }

            if (UnidentifiableExtensions.Contains(extension) || !File.Exists(path) || new FileInfo(path).Length > MaximumHashedBytes)
            {
                return null;
            }

            if (extension == ".zip")
            {
                using ZipArchive archive = ZipFile.OpenRead(path);
                ZipArchiveEntry largest = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).OrderByDescending(entry => entry.Length).FirstOrDefault();
                return largest == null ? null : ("crc", largest.Crc32.ToString("x8"));
            }

            using FileStream stream = File.OpenRead(path);
            using MD5 md5 = MD5.Create();
            return ("md5", Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant());
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is InvalidDataException)
        {
            GD.PrintErr($"[GameInfo] couldn't read {path}: {exception.Message}");
            return null;
        }
    }
}
