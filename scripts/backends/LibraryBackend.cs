using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// What a library source can do beyond listing systems and games. The UI hides the controls for
// anything the active source leaves out.
[Flags]
public enum LibraryFeatures
{
    None = 0,

    // Games live on a server and download before they play. Delete removes the downloaded copy.
    Downloads = 1 << 0,

    // BIOS files download from the source.
    Firmware = 1 << 1,

    Collections = 1 << 2,

    // Favorites can be added and removed.
    Favorites = 1 << 3,

    SaveSync = 1 << 4,

    // Netplay peers match on the server address and the server's game ids.
    Netplay = 1 << 5,

    // Cover art, marquees and screenshots download from the source.
    Artwork = 1 << 6,

    // Systems and games are cached on disk between runs.
    Cache = 1 << 7
}

// One source of systems and games. RomBox uses one source at a time, chosen in setup.
public interface ILibraryBackend
{
    string SourceId { get; }

    string DisplayName { get; }

    LibraryFeatures Features { get; }

    // Checks the saved settings: signs in to a server, or confirms a folder exists.
    Task<(bool isSuccess, string errorMessage)> ConnectAsync(ConfigManager configManager);

    Task<List<GameSystem>> GetSystemsAsync();

    // reportCount receives the number of games found so far.
    Task<List<Game>> GetGamesAsync(GameSystem system, Action<int> reportCount = null);

    Task<List<Collection>> GetCollectionsAsync();

    Task<List<Firmware>> GetFirmwareAsync();
}

public static class LibrarySources
{
    public const string RomM = "RomM";
    public const string Local = "Local";

    public static readonly string[] All = { RomM, Local };

    public static string DisplayName(string sourceId)
    {
        return sourceId == Local ? "Local Folders" : "RomM Server";
    }

    public static string Describe(string sourceId)
    {
        return sourceId == Local
            ? "Games already on this computer or a mounted network share, one folder per system. Works with ES-DE, EmuDeck and RetroArch folder layouts."
            : "A RomM server you or a friend runs. Games download when you play them, and saves sync back to the server.";
    }

    public static ILibraryBackend Create(string sourceId, AppInstance appInstance)
    {
        return sourceId == Local
            ? new LocalFolderBackend()
            : new RomMBackend(appInstance.rommApi);
    }

    public static bool Has(this ILibraryBackend backend, LibraryFeatures feature)
    {
        return backend != null && (backend.Features & feature) == feature;
    }
}
