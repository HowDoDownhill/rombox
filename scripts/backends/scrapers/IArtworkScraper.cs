using System.Collections.Generic;
using System.Threading.Tasks;

// One piece of art a scraper found. AssetType is the name AssetManager reports for RomM art too.
public readonly record struct ScrapedAsset(string AssetType, string Url, string LocalFilePath);

// Finds artwork for games from a source that has none of its own.
public interface IArtworkScraper
{
    string DisplayName { get; }

    // The art available for this game. Empty when nothing matches.
    Task<List<ScrapedAsset>> FindAsync(Game game, string assetsPath);

    Task<bool> DownloadAsync(string url, string destinationFilePath);
}
