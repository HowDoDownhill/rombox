using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

// First-run setup: look, library source and its details, Steam, then the library loads. It runs
// before the login screen on a new install, and again from Settings > General > Run Setup Again.
public partial class SetupWizard : Control
{
    private enum Step { Welcome, Look, Source, RomM, Local, Steam, Done }

    private const string LoadingScenePath = "res://scenes/login/loading_screen.tscn";
    private const float SteamStatusSeconds = 2.0f;
    private const string CaptureArgumentPrefix = "--setup-capture=";
    private const string CaptureStepArgumentPrefix = "--setup-step=";
    private const string CaptureSizeArgumentPrefix = "--ui-capture-size=";

    [Export] private PanelContainer panel;

    [ExportGroup("Panel Shadow")]
    [Export(PropertyHint.Range, "0,120,1")] private int panelShadowSize = MicaShadow.DefaultSize;
    [Export] private Color panelShadowColor = MicaShadow.DefaultColor;
    [Export] private Vector2 panelShadowOffset = MicaShadow.DefaultOffset;

    private AppInstance appInstance;
    private ConfigManager config;

    private Label stepLabel;
    private Label titleLabel;
    private RichTextLabel bodyLabel;
    private VBoxContainer content;
    private Label statusLabel;
    private Button backButton;
    private Button nextButton;

    private Step step = Step.Welcome;
    private bool wasSetUpBefore;
    private bool busy;
    private Control firstFocus;

    private string chosenSource;
    private bool addToSteam;

    private LineEdit hostInput;
    private LineEdit usernameInput;
    private LineEdit passwordInput;
    private LineEdit apiKeyInput;

    private LineEdit folderInput;
    private RichTextLabel scanSummary;
    private LocalLibraryScan lastScan;
    private string lastScannedPath;

    public override void _Ready()
    {
        appInstance = GetNode<AppInstance>("/root/AppInstance");
        config = appInstance.configManager;
        wasSetUpBefore = config.SetupComplete;
        chosenSource = config.LibrarySource;

        // Decided once: answering the Steam step changes the settings these read.
        offersSteamShortcut = SteamShortcuts.IsSupportedPlatform && !SteamShortcuts.IsLaunchedThroughSteam
            && SteamShortcuts.GetState() != SteamShortcutState.SteamNotFound;
        bool asksControllerMethodOnly = OperatingSystem.IsWindows() && SteamShortcuts.IsLaunchedThroughSteam
            && config.ControllerMappingConsent == ConfigManager.ControllerMappingConsentUnasked;
        showsSteamStep = !OS.HasFeature("android") && (offersSteamShortcut || asksControllerMethodOnly);

        Appearance.ApplySaved(config);
        BuildFrame();

        var micaMaterial = GD.Load<ShaderMaterial>("res://assets/materials/mica_panel.tres");
        MicaShadow.AttachToAll(this, micaMaterial, panelShadowColor, panelShadowSize, panelShadowOffset);

        ShowStep(Step.Welcome);
        CaptureIfRequested();
        PressKeysIfRequested();
    }

    // The order of steps follows the choices made so far.
    private List<Step> Steps
    {
        get
        {
            var steps = new List<Step> { Step.Welcome, Step.Look, Step.Source };
            steps.Add(chosenSource == LibrarySources.Local ? Step.Local : Step.RomM);

            if (showsSteamStep)
            {
                steps.Add(Step.Steam);
            }

            steps.Add(Step.Done);
            return steps;
        }
    }

    private bool offersSteamShortcut;

    // Offers the Steam shortcut, or, already running through Steam on Windows, asks only how to set up controllers.
    private bool showsSteamStep;

    private static bool AllowsLocalFolders => !OS.HasFeature("android");

    private void BuildFrame()
    {
        var margin = new MarginContainer();

        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride("margin_" + side, 40);
        }

        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 18);
        margin.AddChild(column);

        stepLabel = new Label { ThemeTypeVariation = "CaptionLabel" };
        column.AddChild(stepLabel);

        titleLabel = new Label { ThemeTypeVariation = "DisplayLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(titleLabel);

        bodyLabel = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FocusMode = FocusModeEnum.None,
            MouseFilter = MouseFilterEnum.Ignore
        };
        column.AddChild(bodyLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            FollowFocus = true
        };
        column.AddChild(scroll);

        content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(content);

        statusLabel = new Label { ThemeTypeVariation = "MutedLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(statusLabel);

        var navigation = new HBoxContainer();
        column.AddChild(navigation);

        backButton = new Button { ThemeTypeVariation = "ListButton", Text = "Back" };
        backButton.Pressed += GoBack;
        navigation.AddChild(backButton);

        navigation.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        nextButton = new Button { ThemeTypeVariation = "ListButton", Text = "Next" };
        nextButton.Pressed += GoNext;
        navigation.AddChild(nextButton);

        // Left to itself, Godot sends right from Back up to the wide rows above.
        backButton.FocusNeighborRight = nextButton.GetPath();
        nextButton.FocusNeighborLeft = backButton.GetPath();
    }

    // Down from the step's last control goes to Next, the button a controller player wants there.
    private void LinkLastControlToNext()
    {
        if (!nextButton.Visible)
        {
            return;
        }

        var focusable = new List<Control>();
        CollectFocusable(content, focusable);

        if (focusable.Count == 0)
        {
            return;
        }

        Control last = focusable[^1];
        IEnumerable<Control> bottomRow = last.GetParent() is HBoxContainer
            ? focusable.Where(control => control.GetParent() == last.GetParent())
            : new[] { last };

        foreach (Control control in bottomRow)
        {
            control.FocusNeighborBottom = nextButton.GetPath();
        }
    }

    // A settings row takes focus for its whole row, so its inner widget doesn't count.
    private static void CollectFocusable(Node node, List<Control> found)
    {
        foreach (Node child in node.GetChildren())
        {
            if (child.IsQueuedForDeletion())
            {
                continue;
            }

            if (child is SettingsListEntry entry)
            {
                found.Add(entry);
            }
            else
            {
                if (child is Control control && control.FocusMode != FocusModeEnum.None)
                {
                    found.Add(control);
                }

                CollectFocusable(child, found);
            }
        }
    }

    private void ShowStep(Step newStep)
    {
        step = newStep;
        firstFocus = null;
        GD.Print($"[Setup] step {step}");

        foreach (Node child in content.GetChildren())
        {
            child.QueueFree();
        }

        List<Step> steps = Steps;
        int position = steps.IndexOf(step);
        stepLabel.Text = position > 0 ? $"Step {position} of {steps.Count - 1}" : "";
        statusLabel.Text = "";
        statusLabel.ThemeTypeVariation = "MutedLabel";
        backButton.Visible = position > 0 || wasSetUpBefore;
        backButton.Text = position > 0 ? "Back" : "Cancel";
        nextButton.Visible = true;
        nextButton.Disabled = false;

        switch (step)
        {
            case Step.Welcome: BuildWelcome(); break;
            case Step.Look: BuildLook(); break;
            case Step.Source: BuildSource(); break;
            case Step.RomM: BuildRomM(); break;
            case Step.Local: BuildLocal(); break;
            case Step.Steam: BuildSteam(); break;
            case Step.Done: BuildDone(); break;
        }

        Control focusTarget = firstFocus ?? (nextButton.Visible ? nextButton : backButton);
        LinkLastControlToNext();

        Callable.From(() =>
        {
            if (IsInstanceValid(focusTarget) && focusTarget.IsVisibleInTree())
            {
                focusTarget.GrabFocus();
            }
        }).CallDeferred();
    }

    private void BuildWelcome()
    {
        titleLabel.Text = wasSetUpBefore ? "Set Up RomBox Again" : "Welcome to RomBox";
        bodyLabel.Text = "RomBox puts your game library on the big screen and starts each game in the right emulator.\n\n"
            + "This takes about a minute. You'll choose how RomBox looks, where your games come from, and whether to start it through Steam. "
            + "Everything here can be changed later in Settings.";
        nextButton.Text = "Get Started";
    }

    private void BuildLook()
    {
        titleLabel.Text = "Choose a Look";
        bodyLabel.Text = "[b]Glass[/b] has an animated background and frosted panels. [b]Simple[/b] uses flat colours and only redraws when something changes, which suits laptops and handhelds.";
        nextButton.Text = "Next";

        CarouselButton styleButton = AddCarouselRow("Style", Appearance.Styles, config.AppStyle);
        styleButton.ItemSelected += index =>
        {
            config.SaveAppStyle(Appearance.Styles[index]);
            ApplyPreview();
        };

        string[] themes = ConfigManager.Themes.Keys.Prepend(ConfigManager.SystemThemeName).ToArray();
        CarouselButton themeButton = AddCarouselRow("Theme", themes, config.AppTheme);
        themeButton.ItemSelected += index =>
        {
            config.SaveAppTheme(themes[index]);
            ApplyPreview();
        };

        CarouselButton viewButton = AddCarouselRow("Game List", ConfigManager.GameListViews, config.GameListView);
        viewButton.ItemSelected += index => config.SaveGameListView(ConfigManager.GameListViews[index]);

        CheckButton soundsCheck = AddCheckRow("Interface Sounds", config.UiSoundsEnabled);
        soundsCheck.Toggled += enabled => config.SaveUiSoundsEnabled(enabled);

        var hint = new Label
        {
            ThemeTypeVariation = "MutedLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = "Match System picks colours from each system's logo once your library is loaded."
        };
        content.AddChild(hint);
    }

    private void ApplyPreview()
    {
        Appearance.ApplySaved(config);
    }

    private void BuildSource()
    {
        titleLabel.Text = "Where Are Your Games?";
        bodyLabel.Text = "RomBox shows one library at a time. You can switch later by running setup again.";
        nextButton.Visible = false;

        foreach (string source in LibrarySources.All)
        {
            if (source == LibrarySources.Local && !AllowsLocalFolders)
            {
                continue;
            }

            string current = source == chosenSource && wasSetUpBefore ? " (Current)" : "";
            Button option = AddOptionButton(LibrarySources.DisplayName(source) + current, LibrarySources.Describe(source));
            option.Pressed += () =>
            {
                chosenSource = source;
                ShowStep(chosenSource == LibrarySources.Local ? Step.Local : Step.RomM);
            };

            if (source == chosenSource)
            {
                firstFocus = option;
            }
        }
    }

    private void BuildRomM()
    {
        titleLabel.Text = "Connect to RomM";
        bodyLabel.Text = "Enter your server's address, then either an API key or your username and password. An API key comes from your RomM profile page.";
        nextButton.Text = "Connect";

        hostInput = AddTextRow("Server Address", config.RomMHost, "https://romm.example.com", false);
        usernameInput = AddTextRow("Username", config.RomMUsername, "Your RomM username", false);
        passwordInput = AddTextRow("Password", config.RomMPassword, "Your RomM password", true);
        apiKeyInput = AddTextRow("API Key", config.RomMApiKey, "Optional, instead of a password", true);
        firstFocus = hostInput;
    }

    private async void ConnectToRomM()
    {
        if (string.IsNullOrWhiteSpace(hostInput.Text))
        {
            ShowStatus("Enter your server's address.", true);
            return;
        }

        SetBusy(true, "Connecting...");
        string host = hostInput.Text.Trim();
        var (isSuccess, errorMessage) = await appInstance.rommApi.AuthenticateAsync(usernameInput.Text, passwordInput.Text, host, apiKeyInput.Text);
        SetBusy(false);

        if (!isSuccess)
        {
            ShowStatus(errorMessage ?? "RomBox couldn't sign in.", true);
            return;
        }

        config.SaveRomMCredentials(host, usernameInput.Text, passwordInput.Text, apiKeyInput.Text);
        config.SaveLibrarySource(LibrarySources.RomM, config.LocalLibraryPath);
        AdvanceFrom(Step.RomM);
    }

    private void BuildLocal()
    {
        titleLabel.Text = "Choose Your Games Folder";
        bodyLabel.Text = "Pick the folder that holds one folder per system, such as [i]snes[/i], [i]psx[/i] or [i]megadrive[/i]. "
            + "ES-DE, EmuDeck and RetroArch folders work as they are. RomBox only reads this folder; it never moves or deletes your games.";
        nextButton.Text = "Next";

        string suggested = !string.IsNullOrEmpty(config.LocalLibraryPath) ? config.LocalLibraryPath : FindLikelyGamesFolder() ?? "";
        folderInput = AddTextRow("Folder", suggested, "The folder holding your system folders", false);
        folderInput.TextChanged += _ => ClearScan();
        folderInput.TextSubmitted += _ => ScanFolder();
        firstFocus = folderInput;

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 12);
        content.AddChild(buttons);

        if (!OS.HasFeature("android"))
        {
            var browseButton = new Button { ThemeTypeVariation = "ListButton", Text = "Browse..." };
            browseButton.Pressed += BrowseForFolder;
            buttons.AddChild(browseButton);
        }

        var scanButton = new Button { ThemeTypeVariation = "ListButton", Text = "Look for Games" };
        scanButton.Pressed += ScanFolder;
        buttons.AddChild(scanButton);

        CheckButton scrapeCheck = AddCheckRow("Download Box Art from libretro", config.ScrapeArtwork);
        scrapeCheck.Toggled += enabled => config.SaveScrapeArtwork(enabled);

        scanSummary = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            FocusMode = FocusModeEnum.None
        };
        content.AddChild(scanSummary);

        if (lastScan != null && lastScannedPath == suggested)
        {
            ShowScanSummary(lastScan);
        }
        else if (!string.IsNullOrEmpty(suggested) && Directory.Exists(suggested))
        {
            ScanFolder();
        }
    }

    private void BrowseForFolder()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            UseNativeDialog = true,
            Title = "Choose Your Games Folder"
        };

        if (Directory.Exists(folderInput.Text))
        {
            dialog.CurrentDir = folderInput.Text;
        }

        dialog.DirSelected += directory =>
        {
            folderInput.Text = directory;
            dialog.QueueFree();
            ScanFolder();
        };
        dialog.Canceled += () => dialog.QueueFree();
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(1200, 800));
    }

    private void ClearScan()
    {
        lastScan = null;
        lastScannedPath = null;

        if (scanSummary != null)
        {
            scanSummary.Text = "";
        }
    }

    private async void ScanFolder()
    {
        string path = folderInput.Text.Trim();

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            ClearScan();
            ShowStatus("That folder wasn't found.", true);
            return;
        }

        SetBusy(true, "Looking for games...");
        LocalLibraryScan scan = await Task.Run(() => LocalLibraryScanner.Scan(path));
        SetBusy(false);

        if (!IsInstanceValid(folderInput) || folderInput.Text.Trim() != path)
        {
            return;
        }

        lastScan = scan;
        lastScannedPath = path;
        ShowScanSummary(scan);
    }

    private void ShowScanSummary(LocalLibraryScan scan)
    {
        var text = new System.Text.StringBuilder();

        if (scan.GameCount == 0)
        {
            text.Append("[b]No games found here.[/b] RomBox looks for folders named after systems, such as snes, psx or megadrive.");
            ShowStatus("", false);
        }
        else
        {
            text.Append($"[b]Found {scan.GameCount.ToString("N0", CultureInfo.InvariantCulture)} games in {scan.Systems.Count} systems.[/b]\n");
            text.Append(string.Join(", ", scan.Systems.Select(system => $"{system.Name} ({system.RomCount.ToString("N0", CultureInfo.InvariantCulture)})")));
        }

        if (scan.UnrecognisedFolders.Count > 0)
        {
            text.Append("\n\n[color=#" + Appearance.CaptionText.ToHtml(false) + "]Not recognised as systems: ");
            text.Append(string.Join(", ", scan.UnrecognisedFolders.Take(12)));
            text.Append(scan.UnrecognisedFolders.Count > 12 ? $" and {scan.UnrecognisedFolders.Count - 12} more" : "");
            text.Append("[/color]");
        }

        scanSummary.Text = text.ToString();
    }

    private void ConfirmLocalFolder()
    {
        string path = folderInput.Text.Trim();

        if (lastScan == null || lastScannedPath != path)
        {
            ScanFolder();
            ShowStatus("Checking the folder first. Press Next again once games are found.", false);
            return;
        }

        if (lastScan.GameCount == 0)
        {
            ShowStatus("Choose a folder with games in it.", true);
            return;
        }

        config.SaveLibrarySource(LibrarySources.Local, path);
        AdvanceFrom(Step.Local);
    }

    // ES-DE, EmuDeck and RetroDECK default locations, the first that exists.
    private static string FindLikelyGamesFolder()
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, "ROMs"),
            Path.Combine(home, "Emulation", "roms"),
            Path.Combine(home, "retrodeck", "roms"),
            "/run/media/mmcblk0p1/Emulation/roms"
        };

        if (OperatingSystem.IsWindows())
        {
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.IsReady)
                    {
                        candidates.Add(Path.Combine(drive.RootDirectory.FullName, "Emulation", "roms"));
                    }
                }
                catch (IOException)
                {
                }
            }
        }

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private void BuildSteam()
    {
        if (!offersSteamShortcut)
        {
            titleLabel.Text = "Set Up Controllers";
            bodyLabel.Text = "RomBox can point each emulator at the right controller for every player through Steam Input.";
            nextButton.Visible = false;

            foreach (ControllerSetupMethod method in ControllerSetupMethod.All)
            {
                string label = method == ControllerSetupMethod.Recommended ? method.Title + " (Recommended)" : method.Title;
                Button option = AddOptionButton(label, method.Description);
                option.Pressed += () =>
                {
                    config.SaveControllerMappingConsent(method.ConsentValue);
                    AdvanceFrom(Step.Steam);
                };
                firstFocus ??= option;
            }

            return;
        }

        SteamShortcutState state = SteamShortcuts.GetState();
        titleLabel.Text = state == SteamShortcutState.Current ? "Start Through Steam" : "Add RomBox to Steam";
        bodyLabel.Text = "Started from Steam, RomBox gets:\n"
            + "• [b]Steam Input.[/b] Every controller works, and each emulator gets the right one for every player.\n"
            + "• [b]Steam Deck Game Mode and Big Picture.[/b]\n"
            + "• [b]The Steam overlay, Remote Play and Steam Link.[/b]\n\n"
            + (SteamShortcuts.IsSteamRunning() && state != SteamShortcutState.Current
                ? "When you finish setup, RomBox closes Steam, adds itself, and starts again from Steam."
                : "When you finish setup, RomBox restarts from Steam.");
        nextButton.Visible = false;

        string acceptLabel = state switch
        {
            SteamShortcutState.Current => "Restart Through Steam (Recommended)",
            SteamShortcutState.Outdated => "Fix the Steam Shortcut (Recommended)",
            _ => "Add to Steam (Recommended)"
        };

        Button accept = AddOptionButton(acceptLabel, "You can still start RomBox outside Steam whenever you like.");
        accept.Pressed += () =>
        {
            addToSteam = true;

            if (OperatingSystem.IsWindows())
            {
                config.SaveControllerMappingConsent(ControllerSetupMethod.SteamInput.ConsentValue);
            }

            AdvanceFrom(Step.Steam);
        };
        firstFocus = accept;

        Button skip = AddOptionButton("Not Now", "Your games still work. You set up controllers in each emulator yourself. Settings > Input Settings > Add to Steam does this later.");
        skip.Pressed += () =>
        {
            addToSteam = false;
            config.SaveOfferSteamLaunch(false);

            if (OperatingSystem.IsWindows())
            {
                config.SaveControllerMappingConsent(ControllerSetupMethod.Manual.ConsentValue);
            }

            AdvanceFrom(Step.Steam);
        };
    }

    private void BuildDone()
    {
        titleLabel.Text = "All Set";
        string library = config.LibrarySource == LibrarySources.Local
            ? $"your games in [b]{EscapeBbcode(config.LocalLibraryPath)}[/b]"
            : $"your RomM server at [b]{EscapeBbcode(config.RomMHost)}[/b]";
        string steam = addToSteam ? " RomBox will restart through Steam first." : "";
        bodyLabel.Text = $"RomBox will load {library}.{steam}\n\nPress the Options button any time for settings, including running this setup again.";
        nextButton.Text = addToSteam ? "Finish and Restart Through Steam" : "Start RomBox";
    }

    private void GoNext()
    {
        if (busy)
        {
            return;
        }

        switch (step)
        {
            case Step.RomM: ConnectToRomM(); break;
            case Step.Local: ConfirmLocalFolder(); break;
            case Step.Done: Finish(); break;
            default: AdvanceFrom(step); break;
        }
    }

    private void AdvanceFrom(Step current)
    {
        List<Step> steps = Steps;
        int index = steps.IndexOf(current);

        if (index >= 0 && index + 1 < steps.Count)
        {
            ShowStep(steps[index + 1]);
        }
    }

    private void GoBack()
    {
        if (busy)
        {
            return;
        }

        List<Step> steps = Steps;
        int index = steps.IndexOf(step);

        if (index > 0)
        {
            ShowStep(steps[index - 1]);
        }
        else if (wasSetUpBefore)
        {
            GetTree().ChangeSceneToFile(LoadingScenePath);
        }
    }

    private async void Finish()
    {
        config.SaveSetupComplete(true);

        if (addToSteam && await RestartThroughSteam())
        {
            return;
        }

        GetTree().ChangeSceneToFile(LoadingScenePath);
    }

    // Returns true when this copy is quitting so Steam's copy takes over.
    private async Task<bool> RestartThroughSteam()
    {
        SetBusy(true, "Adding RomBox to Steam...");
        SteamShortcutState state = SteamShortcuts.GetState();
        bool enableSteamInput = config.ControllerMappingConsent != ConfigManager.ControllerMappingConsentDeclined;

        // Steam reads new shortcuts, artwork and Steam Input settings only while closed.
        if (state != SteamShortcutState.Current && SteamShortcuts.IsSteamRunning())
        {
            SetBusy(true, "Closing Steam...");
            await SteamShortcuts.CloseSteam();

            if (SteamShortcuts.IsSteamRunning())
            {
                SetBusy(false);
                ShowStatus("Steam didn't close, so RomBox wasn't added. Settings > Input Settings > Add to Steam tries again.", true);
                await ToSignal(GetTree().CreateTimer(SteamStatusSeconds * 2), SceneTreeTimer.SignalName.Timeout);
                return false;
            }
        }

        if (!SteamShortcuts.IsSteamRunning())
        {
            SteamShortcutResult result = SteamShortcuts.AddOrUpdateThisApp(enableSteamInput);

            if (result == SteamShortcutResult.SteamNotFound || result == SteamShortcutResult.Failed)
            {
                SetBusy(false);
                ShowStatus("RomBox couldn't add itself to Steam. In Steam, choose Games > Add a Non-Steam Game to My Library.", true);
                await ToSignal(GetTree().CreateTimer(SteamStatusSeconds * 2), SceneTreeTimer.SignalName.Timeout);
                return false;
            }
        }

        SetBusy(true, "Starting RomBox through Steam...");
        SteamShortcuts.LaunchThroughSteam();
        await ToSignal(GetTree().CreateTimer(SteamStatusSeconds), SceneTreeTimer.SignalName.Timeout);
        GD.Print("[Setup] quitting so Steam's copy takes over.");
        GetTree().Quit();
        return true;
    }

    private void SetBusy(bool isBusy, string message = null)
    {
        busy = isBusy;
        nextButton.Disabled = isBusy;
        backButton.Disabled = isBusy;

        if (message != null)
        {
            ShowStatus(message, false);
        }
        else if (!isBusy)
        {
            ShowStatus("", false);
        }
    }

    private void ShowStatus(string message, bool isWarning)
    {
        statusLabel.Text = message;
        statusLabel.ThemeTypeVariation = isWarning ? "WarningLabel" : "MutedLabel";
    }

    private static string EscapeBbcode(string text)
    {
        return (text ?? "").Replace("[", "[lb]");
    }

    // Rows reuse the settings list entry, so focus and highlight match Settings.
    private SettingsListEntry AddRow(string labelText, Control widget)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = labelText, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(label);
        row.AddChild(widget);

        var entry = SettingsEntryScene.Instantiate<SettingsListEntry>();
        entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(row);
        content.AddChild(entry);
        firstFocus ??= entry;
        return entry;
    }

    private static readonly PackedScene SettingsEntryScene = GD.Load<PackedScene>("res://scenes/settings_list/settings_list_entry.tscn");

    private CarouselButton AddCarouselRow(string labelText, IReadOnlyList<string> options, string current)
    {
        var carousel = new CarouselButton { CustomMinimumSize = new Vector2(360, 0), SizeFlagsHorizontal = SizeFlags.ShrinkEnd };

        for (int index = 0; index < options.Count; index++)
        {
            carousel.AddItem(options[index], index);
        }

        int selected = Math.Max(0, options.ToList().IndexOf(current));
        carousel.Select(selected);

        // A click on either half steps that way, since the arrows themselves don't take clicks.
        carousel.MouseFilter = MouseFilterEnum.Stop;
        carousel.GuiInput += inputEvent =>
        {
            if (inputEvent is InputEventMouseButton mouse && mouse.Pressed && mouse.ButtonIndex == MouseButton.Left)
            {
                carousel.Step(mouse.Position.X < carousel.Size.X / 2 ? -1 : 1);
            }
        };

        AddRow(labelText, carousel);
        return carousel;
    }

    private CheckButton AddCheckRow(string labelText, bool pressed)
    {
        var check = new CheckButton { ButtonPressed = pressed };
        AddRow(labelText, check);
        return check;
    }

    private LineEdit AddTextRow(string labelText, string value, string placeholder, bool secret)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(new Label { Text = labelText, CustomMinimumSize = new Vector2(220, 0) });

        var input = new LineEdit
        {
            Text = value ?? "",
            PlaceholderText = placeholder,
            Secret = secret,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddChild(input);
        content.AddChild(row);
        firstFocus ??= input;
        return input;
    }

    private Button AddOptionButton(string title, string description)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);

        var button = new Button { ThemeTypeVariation = "ListButton", Text = title, Alignment = HorizontalAlignment.Left };
        box.AddChild(button);

        var detail = new Label
        {
            ThemeTypeVariation = "MutedLabel",
            Text = description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        var detailMargin = new MarginContainer();
        detailMargin.AddThemeConstantOverride("margin_left", 16);
        detailMargin.AddThemeConstantOverride("margin_bottom", 10);
        detailMargin.AddChild(detail);
        box.AddChild(detailMargin);

        content.AddChild(box);
        return button;
    }

    // Left and right change the focused row's value, and Select toggles or presses it, as in Settings.
    public override void _Input(InputEvent inputEvent)
    {
        if (GetViewport().GuiGetFocusOwner() is not SettingsListEntry entry || busy)
        {
            return;
        }

        if (inputEvent.IsActionPressed("ui_left", true))
        {
            entry.CycleWidget(-1);
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent.IsActionPressed("ui_right", true))
        {
            entry.CycleWidget(1);
            GetViewport().SetInputAsHandled();
        }
        else if (inputEvent.IsActionPressed("ui_accept"))
        {
            entry.InteractWithWidget();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            if (GetViewport().GuiGetFocusOwner() is LineEdit lineEdit && lineEdit.IsEditing())
            {
                return;
            }

            GoBack();
            GetViewport().SetInputAsHandled();
        }
    }

    // --setup-keys=accept,down,right,... sends those ui_ actions in order, to walk the wizard as a
    // controller would. Each key logs what held focus when it was sent.
    private async void PressKeysIfRequested()
    {
        const string prefix = "--setup-keys=";
        string keys = OS.GetCmdlineUserArgs().FirstOrDefault(argument => argument.StartsWith(prefix))?.Substring(prefix.Length);

        if (keys == null)
        {
            return;
        }

        SceneTree tree = GetTree();
        await ToSignal(tree.CreateTimer(1.5), SceneTreeTimer.SignalName.Timeout);

        foreach (string key in keys.Split(','))
        {
            if (!IsInsideTree())
            {
                return;
            }

            Control focused = GetViewport().GuiGetFocusOwner();
            GD.Print($"[Setup] key {key} on {focused?.GetType().Name}:{(focused as Button)?.Text}");
            Input.ParseInputEvent(new InputEventAction { Action = "ui_" + key, Pressed = true });
            await ToSignal(tree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
            Input.ParseInputEvent(new InputEventAction { Action = "ui_" + key, Pressed = false });
            await ToSignal(tree.CreateTimer(0.6), SceneTreeTimer.SignalName.Timeout);
        }
    }

    // --setup-capture=<png> [--setup-step=<n>] [--ui-capture-size=WxH] saves one step as an image
    // and quits, for screenshots and checking layouts.
    private async void CaptureIfRequested()
    {
        string[] arguments = OS.GetCmdlineUserArgs();
        string capturePath = arguments.FirstOrDefault(argument => argument.StartsWith(CaptureArgumentPrefix))?.Substring(CaptureArgumentPrefix.Length);

        if (capturePath == null)
        {
            return;
        }

        string sizeArgument = arguments.FirstOrDefault(argument => argument.StartsWith(CaptureSizeArgumentPrefix));
        string[] sizeParts = sizeArgument?.Substring(CaptureSizeArgumentPrefix.Length).Split('x');

        if (sizeParts?.Length == 2 && int.TryParse(sizeParts[0], out int width) && int.TryParse(sizeParts[1], out int height))
        {
            DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
            DisplayServer.WindowSetSize(new Vector2I(width, height));
            DisplayServer.WindowSetPosition(Vector2I.Zero);
        }

        string stepArgument = arguments.FirstOrDefault(argument => argument.StartsWith(CaptureStepArgumentPrefix))?.Substring(CaptureStepArgumentPrefix.Length);

        if (stepArgument != null && Enum.TryParse(stepArgument, true, out Step requested))
        {
            if (requested == Step.Local)
            {
                chosenSource = LibrarySources.Local;
            }
            else if (requested == Step.RomM)
            {
                chosenSource = LibrarySources.RomM;
            }

            ShowStep(requested);
        }

        await ToSignal(GetTree().CreateTimer(3.0), SceneTreeTimer.SignalName.Timeout);
        QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error result = GetViewport().GetTexture().GetImage().SavePng(capturePath);
        GD.Print($"[Setup] captured {step} to {capturePath}: {result}");
        GetTree().Quit();
    }
}
