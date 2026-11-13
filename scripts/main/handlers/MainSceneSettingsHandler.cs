using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public class MainSceneSettingsHandler
{
    private MainScene mainScene;
    private AppInstance appInstance;
    private static readonly char[] GodotReservedNodeNameCharacters = new[] { '/', ':', '@', '"', '%', '.', '$' };
    private static readonly PackedScene settingsListEntryScene = GD.Load<PackedScene>("res://scenes/settings_list/settings_list_entry.tscn");
    private static readonly PackedScene settingsSectionNavEntryScene = GD.Load<PackedScene>("res://scenes/settings_list/settings_section_nav_entry.tscn");

    public MainSceneSettingsHandler(MainScene mainScene, AppInstance appInstance)
    {
        this.mainScene = mainScene;
        this.appInstance = appInstance;
    }

    public Control SectionsTree => mainScene.settingsSectionsTree;

    public Control OptionsContainer => mainScene.sectionOptionsContainer;

    public void ToggleSettingsMenu()
    {
        mainScene.SectionHandler.ToggleSettings();
    }

    public void FocusFirstSettingsEntry()
    {
        CycleFocusInContainer(mainScene.settingsSectionsTree, 0);
    }

    public void SetupSettingsTree()
    {
        if (mainScene.settingsSectionsTree == null)
        {
            return;
        }

        foreach (Node child in mainScene.settingsSectionsTree.GetChildren())
        {
            mainScene.settingsSectionsTree.RemoveChild(child);
            child.QueueFree();
        }

        if (mainScene.sectionOptionsContainer != null)
        {
            foreach (Node child in mainScene.sectionOptionsContainer.GetChildren())
            {
                mainScene.sectionOptionsContainer.RemoveChild(child);
                child.QueueFree();
            }
        }

        AddNavEntry("General Settings", GenerateGeneralSettingsForm);
        AddNavEntry("Game List Settings", GenerateGameListSettingsForm);
        AddNavEntry("Input Settings", GenerateInputSettingsForm);
        AddNavEntry(SessionSettingsSectionName, GenerateSessionSettingsForm);

        AddNavHeader("Platform Settings");

        if (mainScene.GameListHandler.gameSystems != null)
        {
            foreach (var system in mainScene.GameListHandler.gameSystems)
            {
                AddNavEntry(system.Name, () => GeneratePlatformSettingsForm(system));
            }
        }

        var visibleForm = GetVisibleSettingsForm();
        if (visibleForm == null && mainScene.sectionOptionsContainer?.GetChildCount() > 0)
        {
            if (mainScene.sectionOptionsContainer.GetChild(0) is Control c)
            {
                c.Visible = true;
            }
        }
    }

    private void AddNavHeader(string headerName)
    {
        var label = new Label();
        label.Text = headerName;
        label.ThemeTypeVariation = "CaptionLabel";

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 5);
        margin.AddThemeConstantOverride("margin_left", 5);
        margin.AddChild(label);

        mainScene.settingsSectionsTree.AddChild(margin);
    }

    private void AddNavEntry(string sectionName, Action generateFormAction)
    {
        if (generateFormAction != null) generateFormAction();

        var entry = settingsSectionNavEntryScene.Instantiate<SettingsSectionNavEntry>();
        entry.Setup(sectionName);
        entry.EntrySelected += OnSettingsTreeItemSelected;
        mainScene.settingsSectionsTree.AddChild(entry);
    }

    public Control GetVisibleSettingsForm()
    {
        if (mainScene.sectionOptionsContainer == null)
        {
            return null;
        }

        foreach (Node child in mainScene.sectionOptionsContainer.GetChildren())
        {
            if (child is Control c && c.Visible)
            {
                return c;
            }
        }

        return null;
    }

    public Control FindFirstFocusable(Node node)
    {
        return FocusCycler.FindFirstFocusable(node);
    }

    public void CycleFocusedOption(int direction)
    {
        var focusOwner = mainScene.GetViewport().GuiGetFocusOwner();

        if (focusOwner == null)
        {
            return;
        }

        if (focusOwner is OptionButton optBtn)
        {
            if (optBtn.ItemCount == 0)
            {
                return;
            }

            int newIdx = optBtn.Selected + direction;

            if (newIdx < 0)
            {
                newIdx = optBtn.ItemCount - 1;
            }

            if (newIdx >= optBtn.ItemCount)
            {
                newIdx = 0;
            }

            optBtn.Select(newIdx);
            optBtn.EmitSignal(OptionButton.SignalName.ItemSelected, newIdx);
        }
        else if (focusOwner is BaseButton btn && btn.ToggleMode)
        {
            bool toggled = !btn.ButtonPressed;

            if (direction == 1 && !btn.ButtonPressed)
            {
                toggled = true;
            }
            else if (direction == -1 && btn.ButtonPressed)
            {
                toggled = false;
            }
            else
            {
                return;
            }

            btn.ButtonPressed = toggled;
            btn.EmitSignal(BaseButton.SignalName.Toggled, btn.ButtonPressed);
        }
        else
        {
            SpinBox spinBox = focusOwner as SpinBox ?? focusOwner.GetParent() as SpinBox;
            if (spinBox != null)
            {
                double step = spinBox.Step > 0 ? spinBox.Step : 1;
                double newValue = spinBox.Value + (direction * step);

                if (newValue < spinBox.MinValue)
                {
                    newValue = spinBox.MaxValue;
                }

                if (newValue > spinBox.MaxValue)
                {
                    newValue = spinBox.MinValue;
                }

                spinBox.Value = newValue;
            }
        }
    }

    public void CycleFocusInContainer(Control container, int direction)
    {
        FocusCycler.Cycle(container, direction);
    }

    private void GenerateGeneralSettingsForm()
    {
        if (mainScene.sectionOptionsContainer == null)
        {
            return;
        }

        string nodeName = "GeneralSettings";

        if (mainScene.sectionOptionsContainer.HasNode(nodeName))
        {
            return;
        }

        MarginContainer formContainer = new MarginContainer();
        formContainer.Name = nodeName;
        formContainer.Visible = false;
        formContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.FollowFocus = true;
        formContainer.AddChild(scrollContainer);

        VBoxContainer vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.AddChild(vbox);
        mainScene.sectionOptionsContainer.AddChild(formContainer);

        HBoxContainer libraryBox = new HBoxContainer();
        libraryBox.AddThemeConstantOverride("separation", 16);
        Label libraryLabel = new Label();
        libraryLabel.Text = "Library";
        libraryBox.AddChild(libraryLabel);

        var config = appInstance.configManager;
        Label libraryStatus = new Label();
        libraryStatus.ThemeTypeVariation = "MutedLabel";
        libraryStatus.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        libraryStatus.HorizontalAlignment = HorizontalAlignment.Right;
        libraryStatus.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        libraryStatus.Text = LibrarySources.DisplayName(config.LibrarySource) + ": "
            + (config.LibrarySource == LibrarySources.Local ? config.LocalLibraryPath : config.RomMHost);
        libraryBox.AddChild(libraryStatus);

        // Setup replaces this scene, which would cut off downloads and a running emulator.
        Button runSetupButton = new Button();
        runSetupButton.ThemeTypeVariation = "ListButton";
        runSetupButton.Text = "Run Setup Again";
        runSetupButton.Pressed += () =>
        {
            if (appInstance.downloadManager?.HasUnfinishedTransfers == true || appInstance.emulatorManager.IsEmulatorRunning)
            {
                libraryStatus.Text = "Finish downloads and close the emulator first.";
                return;
            }

            mainScene.GetTree().ChangeSceneToFile(LoginScreen.SetupWizardScenePath);
        };
        libraryBox.AddChild(runSetupButton);

        var libraryEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        libraryEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(libraryBox);
        vbox.AddChild(libraryEntry);

        HBoxContainer styleFieldBox = new HBoxContainer();
        Label styleLabel = new Label();
        styleLabel.Text = "Style";
        styleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        styleFieldBox.AddChild(styleLabel);

        CarouselButton styleOptionButton = new CarouselButton();
        int selectedStyleIdx = 0;

        for (int i = 0; i < Appearance.Styles.Length; i++)
        {
            styleOptionButton.AddItem(Appearance.Styles[i], i);
            if (Appearance.Styles[i] == appInstance.configManager.AppStyle)
            {
                selectedStyleIdx = i;
            }
        }

        styleOptionButton.Select(selectedStyleIdx);
        styleFieldBox.AddChild(styleOptionButton);
        var styleEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        styleEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(styleFieldBox);
        vbox.AddChild(styleEntry);

        HBoxContainer fieldBox = new HBoxContainer();
        Label label = new Label();
        label.Text = "App Theme";
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        fieldBox.AddChild(label);

        CarouselButton themeOptionButton = new CarouselButton();
        int idx = 0;
        int selectedIdx = 0;
        string currentTheme = appInstance.configManager.AppTheme;

        foreach (var theme in System.Linq.Enumerable.Prepend(ConfigManager.Themes.Keys, ConfigManager.SystemThemeName))
        {
            themeOptionButton.AddItem(theme, idx);
            if (theme == currentTheme)
            {
                selectedIdx = idx;
            }
            idx++;
        }

        themeOptionButton.Select(selectedIdx);
        themeOptionButton.ItemSelected += (long index) =>
        {
            string selectedTheme = themeOptionButton.GetItemText((int)index);
            appInstance.configManager.SaveAppTheme(selectedTheme);
            mainScene.ApplyTheme();
        };

        fieldBox.AddChild(themeOptionButton);
        var entry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox);
        vbox.AddChild(entry);

        HBoxContainer backgroundFieldBox = new HBoxContainer();
        Label backgroundLabel = new Label();
        backgroundLabel.Text = "Background Style";
        backgroundLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        backgroundFieldBox.AddChild(backgroundLabel);

        CarouselButton backgroundOptionButton = new CarouselButton();
        int selectedBackgroundIdx = 0;
        string currentBackground = appInstance.configManager.AppBackground;

        for (int i = 0; i < ConfigManager.BackgroundStyles.Length; i++)
        {
            backgroundOptionButton.AddItem(ConfigManager.BackgroundStyles[i].Name, i);
            if (ConfigManager.BackgroundStyles[i].Name == currentBackground)
            {
                selectedBackgroundIdx = i;
            }
        }

        backgroundOptionButton.Select(selectedBackgroundIdx);
        backgroundOptionButton.ItemSelected += (long index) =>
        {
            appInstance.configManager.SaveAppBackground(backgroundOptionButton.GetItemText((int)index));
            mainScene.ApplyTheme();
        };

        backgroundFieldBox.AddChild(backgroundOptionButton);
        var backgroundEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        backgroundEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(backgroundFieldBox);
        backgroundEntry.Visible = appInstance.configManager.AppStyle != Appearance.SimpleStyle;
        vbox.AddChild(backgroundEntry);

        // Simple has one flat background, so the background choice only shows under Glass.
        styleOptionButton.ItemSelected += (long index) =>
        {
            string style = styleOptionButton.GetItemText((int)index);
            appInstance.configManager.SaveAppStyle(style);
            backgroundEntry.Visible = style != Appearance.SimpleStyle;
            mainScene.ApplyTheme();
        };

        HBoxContainer interfaceSizeFieldBox = new HBoxContainer();
        Label interfaceSizeLabel = new Label();
        interfaceSizeLabel.Text = "Interface Size";
        interfaceSizeLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        interfaceSizeFieldBox.AddChild(interfaceSizeLabel);

        CarouselButton interfaceSizeOptionButton = new CarouselButton();
        int selectedInterfaceSizeIdx = 0;

        for (int i = 0; i < ConfigManager.InterfaceSizes.Length; i++)
        {
            interfaceSizeOptionButton.AddItem(ConfigManager.InterfaceSizes[i], i);
            if (ConfigManager.InterfaceSizes[i] == appInstance.configManager.InterfaceSize)
            {
                selectedInterfaceSizeIdx = i;
            }
        }

        interfaceSizeOptionButton.Select(selectedInterfaceSizeIdx);
        interfaceSizeOptionButton.ItemSelected += (long index) =>
        {
            string interfaceSize = interfaceSizeOptionButton.GetItemText((int)index);
            appInstance.configManager.SaveInterfaceSize(interfaceSize);
            InterfaceScale.Apply(mainScene.GetTree().Root, interfaceSize);
        };

        interfaceSizeFieldBox.AddChild(interfaceSizeOptionButton);
        var interfaceSizeEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        interfaceSizeEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(interfaceSizeFieldBox);
        vbox.AddChild(interfaceSizeEntry);

        HBoxContainer uiSoundsFieldBox = new HBoxContainer();
        Label uiSoundsLabel = new Label();
        uiSoundsLabel.Text = "Interface Sounds";
        uiSoundsLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        uiSoundsFieldBox.AddChild(uiSoundsLabel);

        CheckButton uiSoundsCheckbox = new CheckButton();
        uiSoundsCheckbox.ButtonPressed = appInstance.configManager.UiSoundsEnabled;
        uiSoundsCheckbox.Toggled += (bool toggledOn) => appInstance.configManager.SaveUiSoundsEnabled(toggledOn);

        uiSoundsFieldBox.AddChild(uiSoundsCheckbox);
        var uiSoundsEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        uiSoundsEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(uiSoundsFieldBox);
        vbox.AddChild(uiSoundsEntry);

        HBoxContainer preReleaseFieldBox = new HBoxContainer();
        Label preReleaseLabel = new Label();
        preReleaseLabel.Text = "Get Test Builds Early";
        preReleaseLabel.TooltipText = "Updates include test builds with new features before they are finished. They may have problems.";
        preReleaseLabel.MouseFilter = Control.MouseFilterEnum.Stop;
        preReleaseLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        preReleaseFieldBox.AddChild(preReleaseLabel);

        CheckButton preReleaseCheckbox = new CheckButton();
        preReleaseCheckbox.ButtonPressed = appInstance.configManager.PreReleaseBuilds;
        preReleaseCheckbox.Toggled += (bool toggledOn) =>
        {
            appInstance.configManager.SavePreReleaseBuilds(toggledOn);

            // Either way RomBox looks again straight away: on, for a test build to install;
            // off, to offer the latest finished release in place of a test build.
            _ = mainScene.GetNodeOrNull<AppUpdater>("/root/AppUpdater")?.CheckForUpdatesAsync();
        };

        preReleaseFieldBox.AddChild(preReleaseCheckbox);
        var preReleaseEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        preReleaseEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(preReleaseFieldBox);
        vbox.AddChild(preReleaseEntry);

        HBoxContainer discreteGpuFieldBox = new HBoxContainer();
        Label discreteGpuLabel = new Label();
        discreteGpuLabel.Text = "Prefer Discrete GPU";
        discreteGpuLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        discreteGpuFieldBox.AddChild(discreteGpuLabel);

        CheckButton discreteGpuCheckbox = new CheckButton();
        discreteGpuCheckbox.ButtonPressed = appInstance.configManager.PreferDiscreteGpu;

        discreteGpuCheckbox.Toggled += (bool toggledOn) =>
        {
            appInstance.configManager.SavePreferDiscreteGpu(toggledOn);
            DiscreteGpuPreference.RegisterWindowsGpuPreference(OS.GetExecutablePath(), toggledOn);
        };

        discreteGpuFieldBox.AddChild(discreteGpuCheckbox);
        var discreteGpuEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        discreteGpuEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(discreteGpuFieldBox);
        discreteGpuEntry.Visible = OS.HasFeature("windows");
        vbox.AddChild(discreteGpuEntry);
    }

    private const string SessionSettingsSectionName = "Session Settings";

    private static readonly (string Label, int Kbps)[] StreamQualityChoices =
    {
        ("Low (5 Mbit/s)", 5000),
        ("Standard (10 Mbit/s)", 10000),
        ("High (20 Mbit/s)", 20000),
        ("Very High (30 Mbit/s)", 30000),
        ("Highest (50 Mbit/s)", 50000)
    };

    // Choices for Host Session and Join Session: what a host sends, and how a guest shows it.
    private void GenerateSessionSettingsForm()
    {
        if (mainScene.sectionOptionsContainer == null)
        {
            return;
        }

        string nodeName = BuildSectionNodeName(SessionSettingsSectionName);

        if (mainScene.sectionOptionsContainer.HasNode(nodeName))
        {
            return;
        }

        MarginContainer formContainer = new MarginContainer();
        formContainer.Name = nodeName;
        formContainer.Visible = false;
        formContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.FollowFocus = true;
        formContainer.AddChild(scrollContainer);

        VBoxContainer vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.AddChild(vbox);
        mainScene.sectionOptionsContainer.AddChild(formContainer);

        ConfigManager config = appInstance.configManager;

        AddSettingsNote(vbox, "When you host. Changes apply to the next game you start.");

        // A bitrate set by hand in config.cfg shows as the nearest choice.
        int qualityIndex = 0;

        for (int index = 0; index < StreamQualityChoices.Length; index++)
        {
            if (System.Math.Abs(StreamQualityChoices[index].Kbps - config.StreamBitrateKbps) < System.Math.Abs(StreamQualityChoices[qualityIndex].Kbps - config.StreamBitrateKbps))
            {
                qualityIndex = index;
            }
        }

        AddSettingsChoice(vbox, "Picture Quality", StreamQualityChoices.Select(choice => choice.Label).ToArray(), qualityIndex,
            index => config.SaveStreamBitrateKbps(StreamQualityChoices[index].Kbps));

        AddSettingsChoice(vbox, "Picture Size", ConfigManager.StreamPictureHeights.Select(height => height == 2160 ? "2160p (4K)" : $"{height}p").ToArray(),
            System.Math.Max(System.Array.IndexOf(ConfigManager.StreamPictureHeights, config.StreamPictureHeight), 0),
            index => config.SaveStreamPictureHeight(ConfigManager.StreamPictureHeights[index]));

        AddSettingsChoice(vbox, "Games Wider Than 16:9", new[] { "Send the Middle", "Send the Whole Window" }, config.StreamSendsMiddleOfWideGames ? 0 : 1,
            index => config.SaveStreamSendsMiddleOfWideGames(index == 0));

        AddSettingsNote(vbox, "Higher quality and larger sizes need more upload speed. If guests see stutter, choose a lower quality.");

        AddSettingsNote(vbox, "When you join.");

        AddSettingsSwitch(vbox, "Smooth Out an Uneven Connection", config.StreamSmoothsArrival, config.SaveStreamSmoothsArrival);
        AddSettingsSwitch(vbox, "Show Connection Details", config.StreamShowsDetails, config.SaveStreamShowsDetails);

        AddSettingsNote(vbox, "Smoothing holds the picture a few milliseconds longer so motion stays even. Turn it off for the shortest delay.");
    }

    private void AddSettingsChoice(VBoxContainer vbox, string labelText, string[] choices, int selectedIndex, Action<int> onChosen)
    {
        HBoxContainer fieldBox = new HBoxContainer();
        Label label = new Label();
        label.Text = labelText;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        fieldBox.AddChild(label);

        CarouselButton choiceButton = new CarouselButton();

        for (int index = 0; index < choices.Length; index++)
        {
            choiceButton.AddItem(choices[index], index);
        }

        choiceButton.Select(selectedIndex);
        choiceButton.ItemSelected += (long index) => onChosen((int)index);
        fieldBox.AddChild(choiceButton);

        var entry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox);
        vbox.AddChild(entry);
    }

    private void AddSettingsSwitch(VBoxContainer vbox, string labelText, bool isOn, Action<bool> onToggled)
    {
        HBoxContainer fieldBox = new HBoxContainer();
        Label label = new Label();
        label.Text = labelText;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        fieldBox.AddChild(label);

        CheckButton checkbox = new CheckButton();
        checkbox.ButtonPressed = isOn;
        checkbox.Toggled += (bool toggledOn) => onToggled(toggledOn);
        fieldBox.AddChild(checkbox);

        var entry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox);
        vbox.AddChild(entry);
    }

    private static void AddSettingsNote(VBoxContainer vbox, string noteText)
    {
        Label note = new Label();
        note.Text = noteText;
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        note.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        note.Modulate = new Color(1, 1, 1, 0.7f);

        MarginContainer margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        margin.AddChild(note);
        vbox.AddChild(margin);
    }

    private void GenerateGameListSettingsForm()
    {
        if (mainScene.sectionOptionsContainer == null)
        {
            return;
        }

        string nodeName = "GameListSettings";

        if (mainScene.sectionOptionsContainer.HasNode(nodeName))
        {
            return;
        }

        MarginContainer formContainer = new MarginContainer();
        formContainer.Name = nodeName;
        formContainer.Visible = false;
        formContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.FollowFocus = true;
        formContainer.AddChild(scrollContainer);

        VBoxContainer vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.AddChild(vbox);
        mainScene.sectionOptionsContainer.AddChild(formContainer);

        HBoxContainer viewFieldBox = new HBoxContainer();
        Label viewLabel = new Label();
        viewLabel.Text = "Game View";
        viewLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        viewFieldBox.AddChild(viewLabel);

        CarouselButton viewOptionButton = new CarouselButton();

        for (int i = 0; i < ConfigManager.GameListViews.Length; i++)
        {
            viewOptionButton.AddItem(ConfigManager.GameListViews[i], i);
        }

        viewOptionButton.Select(System.Math.Max(0, System.Array.IndexOf(ConfigManager.GameListViews, appInstance.configManager.GameListView)));
        viewOptionButton.ItemSelected += (long index) =>
        {
            appInstance.configManager.SaveGameListView(ConfigManager.GameListViews[index]);
            mainScene.SetGameListView((int)index);
        };

        viewFieldBox.AddChild(viewOptionButton);
        var viewEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        viewEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(viewFieldBox);
        vbox.AddChild(viewEntry);

        HBoxContainer fieldBox = new HBoxContainer();

        Label label = new Label();
        label.Text = "Hide Games Without Box Art";
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        fieldBox.AddChild(label);

        CheckButton checkbox = new CheckButton();
        checkbox.ButtonPressed = appInstance.configManager.HideGamesWithoutBoxArt;

        CheckButton showAllCheckbox = new CheckButton();
        showAllCheckbox.ButtonPressed = appInstance.configManager.ShowAllSystems;

        checkbox.Toggled += (bool toggledOn) =>
        {
            appInstance.configManager.SaveGameListSettings(toggledOn, showAllCheckbox.ButtonPressed);

            if (mainScene.GameListHandler.gameSystems != null && mainScene.GameListHandler.currentGameSystemIndex >= 0 && mainScene.GameListHandler.currentGameSystemIndex < mainScene.GameListHandler.gameSystems.Count)
            {
                mainScene.GameListHandler.SelectSystemByIndex(mainScene.GameListHandler.currentGameSystemIndex);
                mainScene.GameListHandler.OnSystemSelected(mainScene.GameListHandler.gameSystems[mainScene.GameListHandler.currentGameSystemIndex]);
            }
        };
        fieldBox.AddChild(checkbox);

        var entry1 = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry1.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox);
        entry1.Visible = appInstance.Library.Has(LibraryFeatures.Artwork);
        vbox.AddChild(entry1);

        HBoxContainer detailsLayoutBox = new HBoxContainer();
        Label detailsLayoutLabel = new Label();
        detailsLayoutLabel.Text = "Game Details";
        detailsLayoutLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        detailsLayoutBox.AddChild(detailsLayoutLabel);

        CarouselButton detailsLayoutButton = new CarouselButton();
        int selectedDetailsLayout = 0;

        for (int i = 0; i < GameDetailsLayout.Options.Length; i++)
        {
            detailsLayoutButton.AddItem(GameDetailsLayout.Options[i].Label, i);

            if (GameDetailsLayout.Options[i].Id == appInstance.configManager.DetailsLayout)
            {
                selectedDetailsLayout = i;
            }
        }

        detailsLayoutButton.Select(selectedDetailsLayout);
        detailsLayoutButton.ItemSelected += (long index) =>
        {
            string layout = GameDetailsLayout.Options[index].Id;
            appInstance.configManager.SaveDetailsLayout(layout);
            mainScene.GameListHandler.ApplyDetailsLayout(layout);
        };
        detailsLayoutBox.AddChild(detailsLayoutButton);

        var detailsLayoutEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        detailsLayoutEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(detailsLayoutBox);
        vbox.AddChild(detailsLayoutEntry);

        HBoxContainer scrapeBox = new HBoxContainer();
        Label scrapeLabel = new Label();
        scrapeLabel.Text = "Download Box Art";
        scrapeLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrapeBox.AddChild(scrapeLabel);

        CheckButton scrapeCheckbox = new CheckButton();
        scrapeCheckbox.ButtonPressed = appInstance.configManager.ScrapeArtwork;
        scrapeCheckbox.Toggled += (bool toggledOn) => appInstance.configManager.SaveScrapeArtwork(toggledOn);
        scrapeBox.AddChild(scrapeCheckbox);

        var scrapeEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        scrapeEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(scrapeBox);
        scrapeEntry.Visible = !appInstance.Library.Has(LibraryFeatures.Artwork);
        vbox.AddChild(scrapeEntry);

        HBoxContainer gameInfoBox = new HBoxContainer();
        Label gameInfoLabel = new Label();
        gameInfoLabel.Text = "Download Game Info";
        gameInfoLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        gameInfoBox.AddChild(gameInfoLabel);

        CheckButton gameInfoCheckbox = new CheckButton();
        gameInfoCheckbox.ButtonPressed = appInstance.configManager.ScrapeGameInfo;
        gameInfoCheckbox.Toggled += (bool toggledOn) => appInstance.configManager.SaveScrapeGameInfo(toggledOn);
        gameInfoBox.AddChild(gameInfoCheckbox);

        var gameInfoEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        gameInfoEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(gameInfoBox);
        gameInfoEntry.Visible = appInstance.configManager.LibrarySource == LibrarySources.Local;
        vbox.AddChild(gameInfoEntry);

        HBoxContainer fieldBox2 = new HBoxContainer();
        Label label2 = new Label();
        label2.Text = "Show All Systems";
        label2.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        fieldBox2.AddChild(label2);

        showAllCheckbox.Toggled += (bool toggledOn) =>
        {
            appInstance.configManager.SaveGameListSettings(checkbox.ButtonPressed, toggledOn);
            mainScene.GetCache();
            mainScene.GameListHandler.SelectSystemByIndex(0);

            Callable.From(() => {
                SetupSettingsTree();
                if (mainScene.settingsSectionsTree != null && mainScene.settingsSectionsTree.GetChildCount() > 1)
                {
                    if (mainScene.settingsSectionsTree.GetChild(1) is SettingsSectionNavEntry navEntry)
                    {
                        navEntry.GrabFocus();
                        OnSettingsTreeItemSelected(navEntry.SectionName);
                    }
                }
            }).CallDeferred();
        };
        fieldBox2.AddChild(showAllCheckbox);

        var entry2 = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry2.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox2);
        vbox.AddChild(entry2);
    }

    private void GenerateInputSettingsForm()
    {
        if (mainScene.sectionOptionsContainer == null)
        {
            return;
        }

        string nodeName = "InputSettings";

        if (mainScene.sectionOptionsContainer.HasNode(nodeName))
        {
            return;
        }

        MarginContainer formContainer = new MarginContainer();
        formContainer.Name = nodeName;
        formContainer.Visible = false;
        formContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.FollowFocus = true;
        formContainer.AddChild(scrollContainer);

        VBoxContainer vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.AddChild(vbox);
        mainScene.sectionOptionsContainer.AddChild(formContainer);

        HBoxContainer controllerSetupBox = new HBoxContainer();
        Label controllerSetupLabel = new Label();
        controllerSetupLabel.Text = "Controller Setup";
        controllerSetupLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        controllerSetupBox.AddChild(controllerSetupLabel);

        Label controllerSetupStatus = new Label();
        controllerSetupStatus.ThemeTypeVariation = "MutedLabel";
        controllerSetupBox.AddChild(controllerSetupStatus);

        CarouselButton controllerSetupOptionButton = new CarouselButton();

        for (int methodIndex = 0; methodIndex < ControllerSetupMethod.All.Count; methodIndex++)
        {
            controllerSetupOptionButton.AddItem(ControllerSetupMethod.All[methodIndex].Title, methodIndex);
        }

        void ShowStoredSetupMethod()
        {
            ControllerSetupMethod storedSetupMethod = ControllerSetupMethod.FromConsent(appInstance.configManager.ControllerMappingConsent) ?? ControllerSetupMethod.Manual;
            controllerSetupOptionButton.Select(ControllerSetupMethod.IndexOf(storedSetupMethod));
            controllerSetupStatus.Text = DescribeInputLayerAvailability();
        }

        ShowStoredSetupMethod();

        formContainer.VisibilityChanged += () =>
        {
            if (formContainer.Visible)
            {
                ShowStoredSetupMethod();
            }
        };

        controllerSetupOptionButton.ItemSelected += (long index) =>
        {
            mainScene.InputHandler.OnControllerSetupMethodChosen(ControllerSetupMethod.All[(int)index]);
            controllerSetupStatus.Text = DescribeInputLayerAvailability();
        };

        controllerSetupBox.AddChild(controllerSetupOptionButton);
        var controllerSetupEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        controllerSetupEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(controllerSetupBox);
        controllerSetupEntry.Visible = OperatingSystem.IsWindows() && !OS.HasFeature("android");
        vbox.AddChild(controllerSetupEntry);

        HBoxContainer steamLibraryBox = new HBoxContainer();
        Label steamLibraryLabel = new Label();
        steamLibraryLabel.Text = "Steam Library";
        steamLibraryLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        steamLibraryBox.AddChild(steamLibraryLabel);

        Label steamLibraryStatus = new Label();
        steamLibraryStatus.ThemeTypeVariation = "MutedLabel";
        steamLibraryBox.AddChild(steamLibraryStatus);

        Button addToSteamButton = new Button();
        addToSteamButton.ThemeTypeVariation = "ListButton";
        addToSteamButton.Visible = !ControllerSetupMethod.IsLaunchedThroughSteam;
        addToSteamButton.Pressed += mainScene.SteamHandler.StartThroughSteam;
        steamLibraryBox.AddChild(addToSteamButton);

        void ShowSteamLibraryStatus()
        {
            steamLibraryStatus.Text = MainSceneSteamHandler.DescribeLibraryStatus();
            addToSteamButton.Text = MainSceneSteamHandler.DescribeLibraryAction();
        }

        mainScene.SteamHandler.ShortcutChanged += ShowSteamLibraryStatus;
        formContainer.VisibilityChanged += () =>
        {
            if (formContainer.Visible)
            {
                ShowSteamLibraryStatus();
            }
        };

        var steamLibraryEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        steamLibraryEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(steamLibraryBox);
        steamLibraryEntry.Visible = SteamShortcuts.IsSupportedPlatform;
        vbox.AddChild(steamLibraryEntry);

        HBoxContainer countBox = new HBoxContainer();
        Label countLabel = new Label();
        countLabel.Text = "Number of Emulator Close Hotkeys";
        countLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        countBox.AddChild(countLabel);

        SpinBox countSpin = new SpinBox();
        countSpin.MinValue = 1;
        countSpin.MaxValue = 10;
        countSpin.Value = appInstance.configManager.EmulatorCloseHotkeyCount;
        countBox.AddChild(countSpin);

        var entry1 = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry1.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(countBox);
        entry1.Visible = !AndroidApps.IsAvailable;
        vbox.AddChild(entry1);

        HBoxContainer holdBox = new HBoxContainer();
        Label holdLabel = new Label();
        holdLabel.Text = "Seconds to Hold Before Closing";
        holdLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        holdBox.AddChild(holdLabel);

        SpinBox holdSpin = new SpinBox();
        holdSpin.MinValue = 0.5;
        holdSpin.MaxValue = 10.0;
        holdSpin.Step = 0.5;
        holdSpin.Value = appInstance.configManager.EmulatorCloseHoldSeconds;
        holdBox.AddChild(holdSpin);

        holdSpin.ValueChanged += (double holdSeconds) =>
        {
            appInstance.configManager.SaveEmulatorCloseHoldSeconds((float)holdSeconds);
            mainScene.InputHandler.UpdateEmulatorCloseHotkeysBtnText();
        };

        var holdEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        holdEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(holdBox);
        holdEntry.Visible = !AndroidApps.IsAvailable;
        vbox.AddChild(holdEntry);

        mainScene.emulatorCloseHotkeysBtn = new Button();
        mainScene.emulatorCloseHotkeysBtn.ThemeTypeVariation = "ListButton";
        mainScene.InputHandler.UpdateEmulatorCloseHotkeysBtnText();

        mainScene.emulatorCloseHotkeysBtn.Pressed += () =>
        {
            mainScene.InputHandler.expectedEmulatorCloseHotkeysCount = (int)countSpin.Value;
            mainScene.InputHandler.collectedEmulatorCloseHotkeys.Clear();
            mainScene.InputHandler.isListeningForEmulatorCloseHotkeys = true;
            mainScene.emulatorCloseHotkeysBtn.Text = $"Listening... (0/{mainScene.InputHandler.expectedEmulatorCloseHotkeysCount})";
        };

        var entry2 = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry2.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(mainScene.emulatorCloseHotkeysBtn);
        entry2.Visible = !AndroidApps.IsAvailable;
        vbox.AddChild(entry2);

        countSpin.ValueChanged += (double val) =>
        {
            int newCount = (int)val;
            appInstance.configManager.SaveInputSettings(newCount, appInstance.configManager.EmulatorCloseHotkeys);
            mainScene.InputHandler.UpdateEmulatorCloseHotkeysBtnText();
        };
    }

    private void GeneratePlatformSettingsForm(GameSystem system)
    {
        if (mainScene.sectionOptionsContainer == null) return;
        string nodeName = BuildSectionNodeName(system.Name);
        if (mainScene.sectionOptionsContainer.HasNode(nodeName)) return;

        MarginContainer formContainer = new MarginContainer();
        formContainer.Name = nodeName;
        formContainer.Visible = false;
        formContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        ScrollContainer scrollContainer = new ScrollContainer();
        scrollContainer.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scrollContainer.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        scrollContainer.FollowFocus = true;
        formContainer.AddChild(scrollContainer);

        VBoxContainer vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scrollContainer.AddChild(vbox);
        mainScene.sectionOptionsContainer.AddChild(formContainer);

        HBoxContainer prefEmulatorBox = new HBoxContainer();
        Label prefLabel = new Label();
        prefLabel.Text = "Preferred Emulator";
        prefLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        prefEmulatorBox.AddChild(prefLabel);

        CarouselButton emulatorOptionButton = new CarouselButton();
        prefEmulatorBox.AddChild(emulatorOptionButton);

        var entry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(prefEmulatorBox);
        vbox.AddChild(entry);

        MarginContainer emulatorSettingsContainer = new MarginContainer();
        vbox.AddChild(emulatorSettingsContainer);

        MarginContainer controllerMappingsContainer = new MarginContainer();
        vbox.AddChild(controllerMappingsContainer);

        List<string> supportedEmulators = appInstance.emulatorManager.GetSupportedEmulators(system.Slug);
        var allEmulators = appInstance.emulatorManager.GetAllAvailableEmulators();

        int idx = 0;
        int selectedIdx = 0;
        string currentPref = appInstance.emulatorManager.GetMappedEmulator(system.Slug);

        foreach (string emuSlug in supportedEmulators)
        {
            if (allEmulators.ContainsKey(emuSlug))
            {
                emulatorOptionButton.AddItem(allEmulators[emuSlug].Name, idx);
                emulatorOptionButton.SetItemMetadata(idx, emuSlug);

                if (emuSlug == currentPref || (currentPref == null && idx == 0))
                {
                    selectedIdx = idx;
                    currentPref = emuSlug;
                }
                idx++;
            }
        }

        if (idx == 0)
        {
            emulatorOptionButton.AddItem("No mapped emulator", 0);
            emulatorOptionButton.Disabled = true;
        }
        else
        {
            emulatorOptionButton.Select(selectedIdx);
            if (allEmulators.ContainsKey(currentPref))
            {
                GenerateEmulatorSettingsUI(currentPref, allEmulators[currentPref], emulatorSettingsContainer, system.Slug);
                mainScene.InputHandler.GeneratePlatformControllerMappingsUI(system.Slug, allEmulators[currentPref], controllerMappingsContainer);
            }

            emulatorOptionButton.ItemSelected += (long index) =>
            {
                string selectedSlug = emulatorOptionButton.GetItemMetadata((int)index).AsString();
                appInstance.configManager.SavePreferredEmulator(system.Slug, selectedSlug);
                if (allEmulators.ContainsKey(selectedSlug))
                {
                    GenerateEmulatorSettingsUI(selectedSlug, allEmulators[selectedSlug], emulatorSettingsContainer, system.Slug);
                    mainScene.InputHandler.GeneratePlatformControllerMappingsUI(system.Slug, allEmulators[selectedSlug], controllerMappingsContainer);
                }
            };
        }
    }

    private void GenerateEmulatorSettingsUI(string slug, EmulatorMeta meta, Control parentContainer, string systemSlug = null)
    {
        foreach (Node child in parentContainer.GetChildren())
        {
            child.QueueFree();
        }

        var selectableCores = meta.GetSelectableCores(systemSlug);
        var emulatorsHoldingSaves = systemSlug == null
            ? new List<string>()
            : appInstance.emulatorManager.GetEmulatorsHoldingSavesForSystem(systemSlug, slug);

        if (meta.SettingsFields == null && selectableCores.Count == 0 && emulatorsHoldingSaves.Count == 0) return;

        VBoxContainer vbox = new VBoxContainer();
        parentContainer.AddChild(vbox);

        if (emulatorsHoldingSaves.Count > 0)
        {
            AddSavesStayWithOtherEmulatorWarning(vbox, meta.Name, emulatorsHoldingSaves);
        }

        if (selectableCores.Count > 0)
        {
            AddCoreSelector(vbox, meta, systemSlug, selectableCores);
        }

        if (meta.SettingsFields == null) return;

        var userSettings = appInstance.emulatorManager.LoadEmulatorSettings(slug);

        string currentOperatingSystem = OS.GetName().ToLower();

        foreach (var field in meta.SettingsFields)
        {
            if (string.IsNullOrEmpty(field.Id) || !field.AppliesTo(currentOperatingSystem)) continue;

            bool hasValue = userSettings.TryGetValue(field.Id, out System.Text.Json.JsonElement element);

            HBoxContainer fieldBox = new HBoxContainer();
            Label label = new Label();
            label.Text = field.Label;
            label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            fieldBox.AddChild(label);

            if (field.Type == "boolean")
            {
                CheckButton checkbox = new CheckButton();
                bool val = field.DefaultValueBool;
                if (hasValue && element.ValueKind == System.Text.Json.JsonValueKind.True) val = true;
                if (hasValue && element.ValueKind == System.Text.Json.JsonValueKind.False) val = false;
                checkbox.ButtonPressed = val;
                checkbox.Toggled += (bool toggledOn) => { appInstance.emulatorManager.SaveEmulatorSetting(slug, field.Id, toggledOn); };
                fieldBox.AddChild(checkbox);
            }
            else if (field.Type == "string")
            {
                LineEdit lineEdit = new LineEdit();
                lineEdit.CustomMinimumSize = new Vector2(200, 0);
                string val = field.DefaultValueString;
                if (hasValue && element.ValueKind == System.Text.Json.JsonValueKind.String) val = element.GetString();
                lineEdit.Text = val;
                lineEdit.TextChanged += (string newText) => { appInstance.emulatorManager.SaveEmulatorSetting(slug, field.Id, newText); };
                fieldBox.AddChild(lineEdit);
            }
            else if (field.Type == "dropdown")
            {
                CarouselButton optionButton = new CarouselButton();
                string val = field.DefaultValueString;
                if (hasValue && element.ValueKind == System.Text.Json.JsonValueKind.String) val = element.GetString();
                int idx = 0; int selectedIdx = 0;
                if (field.Options != null)
                {
                    foreach (var option in field.Options)
                    {
                        optionButton.AddItem(option.Key, idx);
                        optionButton.SetItemMetadata(idx, option.Value);
                        if (option.Value == val) selectedIdx = idx;
                        idx++;
                    }
                }
                optionButton.Select(selectedIdx);
                optionButton.ItemSelected += (long index) => {
                    string selectedValue = optionButton.GetItemMetadata((int)index).AsString();
                    appInstance.emulatorManager.SaveEmulatorSetting(slug, field.Id, selectedValue);
                };
                fieldBox.AddChild(optionButton);
            }

            var entry = settingsListEntryScene.Instantiate<SettingsListEntry>();
            entry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(fieldBox);
            vbox.AddChild(entry);
        }
    }

    private void AddSavesStayWithOtherEmulatorWarning(VBoxContainer vbox, string selectedEmulatorDisplayName, List<string> emulatorsHoldingSaves)
    {
        Label warningLabel = new Label();
        warningLabel.Text = $"{string.Join(" and ", emulatorsHoldingSaves)} already has saves for this system. "
            + $"{selectedEmulatorDisplayName} keeps its own saves, so those will not carry over.";
        warningLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        warningLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        warningLabel.ThemeTypeVariation = "WarningLabel";

        var warningEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        warningEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(warningLabel);
        vbox.AddChild(warningEntry);
    }

    private void AddCoreSelector(VBoxContainer vbox, EmulatorMeta meta, string systemSlug, List<string> selectableCores)
    {
        HBoxContainer coreBox = new HBoxContainer();
        Label coreLabel = new Label();
        coreLabel.Text = "Core";
        coreLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        coreBox.AddChild(coreLabel);

        CarouselButton coreOptionButton = new CarouselButton();
        string selectedCore = appInstance.emulatorManager.ResolveSelectedCore(meta, systemSlug);
        int selectedCoreIndex = 0;

        for (int coreIndex = 0; coreIndex < selectableCores.Count; coreIndex++)
        {
            coreOptionButton.AddItem(selectableCores[coreIndex], coreIndex);
            coreOptionButton.SetItemMetadata(coreIndex, selectableCores[coreIndex]);

            if (selectableCores[coreIndex] == selectedCore)
            {
                selectedCoreIndex = coreIndex;
            }
        }

        coreOptionButton.Select(selectedCoreIndex);

        string shownCore = selectedCore;
        CheckButton coreInstalledCheckbox = null;

        coreOptionButton.ItemSelected += (long index) =>
        {
            shownCore = coreOptionButton.GetItemMetadata((int)index).AsString();
            appInstance.configManager.SavePreferredCore(systemSlug, shownCore);
            coreInstalledCheckbox?.SetPressedNoSignal(appInstance.configManager.IsAndroidCoreSetUp(shownCore));
        };

        coreBox.AddChild(coreOptionButton);

        var coreEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        coreEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(coreBox);
        vbox.AddChild(coreEntry);

        if (!AndroidApps.IsAvailable || string.IsNullOrEmpty(selectedCore))
        {
            return;
        }

        HBoxContainer installedBox = new HBoxContainer();
        Label installedLabel = new Label();
        installedLabel.Text = "Installed in RetroArch";
        installedLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        installedBox.AddChild(installedLabel);

        coreInstalledCheckbox = new CheckButton();
        coreInstalledCheckbox.ButtonPressed = appInstance.configManager.IsAndroidCoreSetUp(shownCore);
        coreInstalledCheckbox.Toggled += (bool isInstalled) =>
        {
            if (isInstalled)
            {
                appInstance.configManager.SaveAndroidCoreSetUp(shownCore);
            }

            else
            {
                appInstance.configManager.ForgetAndroidCoreSetUp(shownCore);
            }
        };
        installedBox.AddChild(coreInstalledCheckbox);

        var installedEntry = settingsListEntryScene.Instantiate<SettingsListEntry>();
        installedEntry.GetNode<MarginContainer>("PanelContainer/ContentMargin").AddChild(installedBox);
        vbox.AddChild(installedEntry);
    }

    private static string BuildSectionNodeName(string sectionName)
    {
        var nodeNameBuilder = new System.Text.StringBuilder();

        foreach (char sectionNameCharacter in sectionName ?? "")
        {
            if (!char.IsWhiteSpace(sectionNameCharacter) && Array.IndexOf(GodotReservedNodeNameCharacters, sectionNameCharacter) < 0)
            {
                nodeNameBuilder.Append(sectionNameCharacter);
            }
        }

        return nodeNameBuilder.Length == 0 ? "Section" : nodeNameBuilder.ToString();
    }

    // Shows one section's options, as choosing it in the list does.
    public void ShowSettingsSection(string sectionName)
    {
        OnSettingsTreeItemSelected(sectionName);
    }

    private void OnSettingsTreeItemSelected(string sectionName)
    {
        if (mainScene.settingsSectionsTree == null || mainScene.sectionOptionsContainer == null)
        {
            return;
        }

        foreach (Node child in mainScene.sectionOptionsContainer.GetChildren())
        {
            if (child is Control control)
            {
                control.Visible = false;
            }
        }

        string nodeName = BuildSectionNodeName(sectionName);
        var activePanel = mainScene.sectionOptionsContainer.GetNodeOrNull<Control>(nodeName);

        if (activePanel != null)
        {
            activePanel.Visible = true;
        }
    }

    private string DescribeInputLayerAvailability()
    {
        if (appInstance.inputLayer == null)
        {
            return "Unavailable";
        }

        if (appInstance.inputLayer.ResolveConsent() != InputLayerConsent.SteamInput)
        {
            return "Emulators Use Their Own Controller Settings";
        }

        return ControllerSetupMethod.IsLaunchedThroughSteam ? "Steam Handles Your Controllers" : "Start the App From Steam to Use It";
    }

}
