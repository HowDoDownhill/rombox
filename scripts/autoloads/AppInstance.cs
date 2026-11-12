using Godot;
using System;

public partial class AppInstance : Node
{
    public ConfigManager configManager;
    public RomMAPI rommApi; 
    public DownloadManager downloadManager;
    public CacheManager cacheManager;
    public EmulatorManager emulatorManager;
    public DataBus dataBus;
    public AssetManager assetManager;
    public SaveSyncManager saveSyncManager;
    public ControllerManager controllerManager;
    public NetplayManager netplayManager;
    public NetplayLobby netplayLobby;
    public NetplayDiscovery netplayDiscovery;
    public NetplayPortMapper netplayPortMapper;
    public InputLayer inputLayer;

    // A running game, controller session or stream from a host has the controller. RomBox still
    // receives its events.
    public bool IsControllerInUseElsewhere =>
        (emulatorManager != null && emulatorManager.IsEmulatorRunning)
        || (inputLayer != null && inputLayer.IsSessionActive)
        || StreamGuestView.IsOpen;

    private ILibraryBackend library;

    // The source the library comes from, following the saved setting.
    public ILibraryBackend Library
    {
        get
        {
            string source = configManager?.LibrarySource ?? LibrarySources.RomM;

            if (library == null || library.SourceId != source)
            {
                library = LibrarySources.Create(source, this);
            }

            return library;
        }
    }

    public override void _Ready()
    {
        StartupTimeline.Mark("first autoload");
        configManager = GetNode<ConfigManager>("/root/ConfigManager");
        rommApi = GetNode<RomMAPI>("/root/RomMAPI");
        downloadManager = GetNode<DownloadManager>("/root/DownloadManager");
        cacheManager = GetNode<CacheManager>("/root/CacheManager");
        emulatorManager = GetNode<EmulatorManager>("/root/EmulatorManager");
        SelectionHighlighter.Attach(GetViewport());
        dataBus = GetNode<DataBus>("/root/DataBus");
        controllerManager = GetNode<ControllerManager>("/root/ControllerManager");

        if (LegacyNameMigration.RelaunchUnderNewNameIfNeeded())
        {
            GetTree().Quit();
            return;
        }

        if (SteamShortcuts.RunCommandLineAddIfRequested())
        {
            GetTree().Quit();
            return;
        }

        LegacyNameMigration.RemoveLegacyFilesInBackground();
        AddChild(new UiSounds(this));
        StreamCommandLine.StartIfRequested(this);
        Callable.From(ApplyDiscreteGpuPreference).CallDeferred();
    }

    private void ApplyDiscreteGpuPreference()
    {
        DiscreteGpuPreference.RegisterWindowsGpuPreference(OS.GetExecutablePath(), configManager.PreferDiscreteGpu);

        bool relaunchOnDiscreteGpu = DiscreteGpuPreference.ShouldRelaunchOnDiscreteGpu(configManager.PreferDiscreteGpu);
        bool relaunchOnX11 = SteamDisplayDriver.ShouldRelaunchOnX11();

        if ((relaunchOnDiscreteGpu || relaunchOnX11)
            && DiscreteGpuPreference.Relaunch(relaunchOnDiscreteGpu, relaunchOnX11 ? SteamDisplayDriver.X11DriverName : DisplayServer.GetName().ToLower()))
        {
            GetTree().Quit();
        }
    }
}
