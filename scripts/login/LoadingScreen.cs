using Godot;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileAccess = System.IO.FileAccess;

public partial class LoadingScreen : Control
{
    [Export] private ProgressBar progressBar;
    [Export] private Label statusLabel;

    [ExportGroup("Panel Shadow")]
    [Export(PropertyHint.Range, "0,120,1")] private int panelShadowSize = MicaShadow.DefaultSize;
    [Export] private Color panelShadowColor = MicaShadow.DefaultColor;
    [Export] private Vector2 panelShadowOffset = MicaShadow.DefaultOffset;

    private AppInstance appInstance;

    public override void _Ready()
    {
        StartupTimeline.Mark("loading screen");
        appInstance = GetNode<AppInstance>("/root/AppInstance");

        Appearance.ApplySaved(appInstance.configManager);

        var micaMaterial = GD.Load<ShaderMaterial>("res://assets/materials/mica_panel.tres");
        MicaShadow.AttachToAll(this, micaMaterial, panelShadowColor, panelShadowSize, panelShadowOffset);

        AttemptLoadFromCacheAsync();
    }

    private async void AttemptLoadFromCacheAsync()
    {
        if (statusLabel != null)
        {
            statusLabel.Text = "Checking cache...";
        }

        var startupTimer = System.Diagnostics.Stopwatch.StartNew();
        ILibraryBackend library = appInstance.Library;

        // A source without a cache is read fresh at every start. Its firmware list comes from the
        // systems it finds, so nothing is requested ahead of the scan.
        if (!library.Has(LibraryFeatures.Cache))
        {
            var (isConnected, errorMessage) = await library.ConnectAsync(appInstance.configManager);

            if (!isConnected)
            {
                appInstance.dataBus.systems = new List<GameSystem>();
                appInstance.dataBus.gameCache.Clear();
                await ShowMessageThenContinue(errorMessage);
                return;
            }

            PreloadDataAsync();
            return;
        }

        Task<Dictionary<int, List<Firmware>>> firmwareRequest = GetFirmwareByPlatformAsync();
        Task<List<Collection>> collectionsRequest = library.GetCollectionsAsync();

        var (cachedSystems, cachedGames) = await Task.Run(() => appInstance.cacheManager.LoadCache());
        long cacheMilliseconds = startupTimer.ElapsedMilliseconds;

        if (cachedSystems != null && cachedSystems.Any() && cachedGames != null && cachedGames.Any())
        {
            foreach (var system in cachedSystems)
            {
                if (cachedGames.TryGetValue(system.Id, out var games))
                {
                    foreach (var game in games)
                    {
                        game.System = system;
                    }
                }
            }
            
            appInstance.dataBus.systems = cachedSystems;
            appInstance.dataBus.gameCache = cachedGames;
            
            if (statusLabel != null)
            {
                statusLabel.Text = "Loaded from cache!";
            }


            if (progressBar != null)
            {
                progressBar.Value = 100;
            }

            await firmwareRequest;
            await SyncFirmwareAsync();
            await PopulateAvailableFirmwareAsync();
            long firmwareMilliseconds = startupTimer.ElapsedMilliseconds - cacheMilliseconds;

            await LoadCollectionsAsync(collectionsRequest);
            long collectionsMilliseconds = startupTimer.ElapsedMilliseconds - cacheMilliseconds - firmwareMilliseconds;

            GD.Print($"[Startup] {StartupTimeline.Describe()}; cache {cacheMilliseconds} ms, then firmware +{firmwareMilliseconds} ms, collections +{collectionsMilliseconds} ms ({appInstance.dataBus.systems.Count} systems; requests overlapped the cache read)");

            GetTree().ChangeSceneToFile("res://scenes/main_scene.tscn");
        }

        else
        {
            PreloadDataAsync();
        }
    }

    private async Task ShowMessageThenContinue(string message)
    {
        if (statusLabel != null)
        {
            statusLabel.Text = message;
        }

        GD.Print($"[Library] {message}");
        await Task.Delay(MessageVisibleMilliseconds);
        GetTree().ChangeSceneToFile("res://scenes/main_scene.tscn");
    }

    private const int MessageVisibleMilliseconds = 3000;

    private async void PreloadDataAsync()
    {
        ILibraryBackend library = appInstance.Library;

        if (statusLabel != null)
        {
            statusLabel.Text = library.Has(LibraryFeatures.Cache) ? "Loading systems..." : "Looking for games...";
        }

        List<GameSystem> systems = await library.GetSystemsAsync();
        appInstance.dataBus.systems = systems ?? new List<GameSystem>();

        if (systems == null || !systems.Any())
        {
            appInstance.dataBus.gameCache.Clear();
            await ShowMessageThenContinue(library.SourceId == LibrarySources.Local
                ? $"No games found in {appInstance.configManager.LocalLibraryPath}. Check that it has a folder for each console, like snes or psx."
                : "No systems found.");
            return;
        }

        if (statusLabel != null)
        {
            statusLabel.Text = "Loading games...";
        }

        appInstance.dataBus.gameCache.Clear();
        int systemsProcessed = 0;

        foreach (var system in systems)
        {
            List<Game> allGamesForSystem = await library.GetGamesAsync(system);
            appInstance.dataBus.gameCache[system.Id] = allGamesForSystem;
            GD.Print($"Found {allGamesForSystem.Count} games for {system.Name}");
            
            systemsProcessed++;

            if (progressBar != null)
            {
                progressBar.Value = ((float)systemsProcessed / systems.Count) * 100;
            }
        }
        
        if (library.Has(LibraryFeatures.Cache))
        {
            GD.Print($"Saving {appInstance.dataBus.gameCache.Sum(x => x.Value.Count)} games to cache.");
            appInstance.cacheManager.SaveCache(appInstance.dataBus.systems, appInstance.dataBus.gameCache);
        }

        await SyncFirmwareAsync();

        await PopulateAvailableFirmwareAsync();

        await LoadCollectionsAsync();


        if (statusLabel != null)
        {
            statusLabel.Text = "Finished!";
        }
        
        await Task.Delay(200);
        GetTree().ChangeSceneToFile("res://scenes/main_scene.tscn");
    }

    private async Task LoadCollectionsAsync(Task<List<Collection>> startedRequest = null)
    {
        if (statusLabel != null)
        {
            statusLabel.Text = "Loading collections...";
        }

        var collections = await (startedRequest ?? appInstance.Library.GetCollectionsAsync()) ?? new List<Collection>();
        appInstance.dataBus.collectionSystems = CollectionProjection.Project(collections, appInstance.dataBus.gameCache);

        var favoriteCollection = collections?.FirstOrDefault(collection => collection.IsFavorite);
        appInstance.dataBus.favoriteCollectionId = favoriteCollection?.Id ?? 0;
        appInstance.dataBus.favoriteRomIds = favoriteCollection?.RomIds == null
            ? new System.Collections.Generic.HashSet<int>()
            : new System.Collections.Generic.HashSet<int>(favoriteCollection.RomIds);

        GD.Print($"Loaded {appInstance.dataBus.collectionSystems.Count} collections, {appInstance.dataBus.favoriteRomIds.Count} favorites.");

        foreach (var collectionSystem in appInstance.dataBus.collectionSystems)
        {
            int cachedGameCount = appInstance.dataBus.gameCache.TryGetValue(collectionSystem.Id, out var cachedCollectionGames) ? cachedCollectionGames.Count : -1;
            GD.Print($"  collection id={collectionSystem.Id} slug={collectionSystem.Slug} games={cachedGameCount} favorite={collectionSystem.IsFavoriteCollection} name=\"{collectionSystem.Name}\"");
        }
    }

    private async Task SyncFirmwareAsync()
    {
        if (!appInstance.Library.Has(LibraryFeatures.Firmware))
        {
            return;
        }

        if (statusLabel != null)
        {
            statusLabel.Text = "Checking for BIOS updates...";
        }

        var firmwareToDownload = new List<(Firmware fw, string slug, string systemName)>();
        var firmwareByPlatform = await GetFirmwareByPlatformAsync();

        foreach (var system in appInstance.dataBus.systems)
        {
            firmwareByPlatform.TryGetValue(system.Id, out List<Firmware> systemFirmware);

            if (systemFirmware != null && systemFirmware.Any())
            {
                foreach (var fw in systemFirmware)
                {
                    firmwareToDownload.Add((fw, system.Slug, system.Name));
                }
            }
        }

        if (!firmwareToDownload.Any())
        {
            return;
        }

        int processed = 0;
        var authHeaders = appInstance.rommApi.GetAuthHeaders();

        foreach (var item in firmwareToDownload)
        {
            Firmware fw = item.fw;
            string slug = item.slug;
            
            string systemBiosDir = Path.Combine(appInstance.configManager.BiosPath, slug);

            if (!Directory.Exists(systemBiosDir))
            {
                Directory.CreateDirectory(systemBiosDir);
            }

            string savePath = Path.Combine(systemBiosDir, fw.FileName);

            if (!File.Exists(savePath))
            {
                if (statusLabel != null)
                {
                    statusLabel.Text = $"Downloading BIOS: {fw.FileName} ({item.systemName})...";
                }

                string downloadUrl = appInstance.rommApi.GetFirmwareDownloadUrl(fw);

                await DownloadFirmwareWrapperAsync(downloadUrl, savePath, authHeaders);

                string configPath = Path.Combine(systemBiosDir, $"{Path.GetFileNameWithoutExtension(fw.FileName)}.config.json");
                string localConfig = "{ \"loaded\": true, \"path\": \"./" + fw.FileName + "\" }";
                await File.WriteAllTextAsync(configPath, localConfig);
            }

            processed++;

            if (progressBar != null)
            {
                progressBar.Value = ((float)processed / firmwareToDownload.Count) * 100;
            }
        }
    }
    
    private Dictionary<int, List<Firmware>> firmwareByPlatformId;

    private async Task<Dictionary<int, List<Firmware>>> GetFirmwareByPlatformAsync()
    {
        if (firmwareByPlatformId != null)
        {
            return firmwareByPlatformId;
        }

        List<Firmware> allFirmware = await appInstance.Library.GetFirmwareAsync() ?? new List<Firmware>();
        firmwareByPlatformId = allFirmware.GroupBy(firmware => firmware.PlatformId).ToDictionary(group => group.Key, group => group.ToList());
        return firmwareByPlatformId;
    }

    private async Task PopulateAvailableFirmwareAsync()
    {
        var firmwareByPlatform = await GetFirmwareByPlatformAsync();

        foreach (var system in appInstance.dataBus.systems)
        {
            var firmwareDir = appInstance.configManager.BiosPath.PathJoin(system.Slug);

            if (DirAccess.DirExistsAbsolute(firmwareDir))
            {
                var firmwaresFromApi = firmwareByPlatform.TryGetValue(system.Id, out List<Firmware> platformFirmware) ? platformFirmware : new List<Firmware>();
                var localFiles = DirAccess.GetFilesAt(firmwareDir);

                var availableFirmwares = new List<Firmware>();

                foreach (var fw in firmwaresFromApi)
                {
                    if (localFiles.Contains(fw.FileName))
                    {
                        fw.FullPath = firmwareDir.PathJoin(fw.FileName);
                        availableFirmwares.Add(fw);
                    }
                }

                system.AvailableFirmwares = availableFirmwares;

                if (!EmulatorManager.IsUsableFirmwarePath(system.PrefferedFirmware) && system.AvailableFirmwares.Any())
                {
                    system.PrefferedFirmware = system.AvailableFirmwares.First().FullPath;
                }
            }
        }
    }
    
    private Task<string> DownloadFirmwareWrapperAsync(string url, string destinationPath, string[] headers)
    {
        var tcs = new TaskCompletionSource<string>();
        
        appInstance.downloadManager.DownloadFile(
            url, 
            destinationPath, 
            headers, 
            (path) => 
            {
                tcs.SetResult(path);
            }

        );
        
        return tcs.Task;
    }
}
