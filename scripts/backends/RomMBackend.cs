using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// A RomM server through RomMAPI. Downloads, save sync, artwork and netplay still call RomMAPI
// directly; they only run when this backend is active.
public sealed class RomMBackend : ILibraryBackend
{
    private const int PageSize = 100;

    private readonly RomMAPI api;

    public RomMBackend(RomMAPI api)
    {
        this.api = api;
    }

    public string SourceId => LibrarySources.RomM;

    public string DisplayName => LibrarySources.DisplayName(LibrarySources.RomM);

    public LibraryFeatures Features =>
        LibraryFeatures.Downloads | LibraryFeatures.Firmware | LibraryFeatures.Collections | LibraryFeatures.Favorites
        | LibraryFeatures.SaveSync | LibraryFeatures.Netplay | LibraryFeatures.Artwork | LibraryFeatures.Cache;

    public Task<(bool isSuccess, string errorMessage)> ConnectAsync(ConfigManager configManager)
    {
        return api.AuthenticateAsync(configManager.RomMUsername, configManager.RomMPassword, configManager.RomMHost, configManager.RomMApiKey);
    }

    public Task<List<GameSystem>> GetSystemsAsync()
    {
        return api.GetSystemsAsync();
    }

    public async Task<List<Game>> GetGamesAsync(GameSystem system, Action<int> reportCount = null)
    {
        var games = new List<Game>();
        int page = 1;

        while (true)
        {
            GameResponse response = await api.GetGamesAsync(system, page, PageSize);

            if (response?.Games == null || response.Games.Count == 0)
            {
                break;
            }

            foreach (Game game in response.Games)
            {
                game.System = system;
                games.Add(game);
            }

            reportCount?.Invoke(games.Count);

            if (games.Count >= response.Total)
            {
                break;
            }

            page++;
        }

        return games;
    }

    public Task<List<Collection>> GetCollectionsAsync()
    {
        return api.GetCollectionsAsync();
    }

    public Task<List<Firmware>> GetFirmwareAsync()
    {
        return api.GetFirmwareAsync();
    }
}
