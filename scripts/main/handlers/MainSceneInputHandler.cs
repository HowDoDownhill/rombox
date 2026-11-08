using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class MainSceneInputHandler
{
    private MainScene mainScene;
    private AppInstance appInstance;

    public bool isListeningForInput = false;
    public Action<string> inputListenCallback;

    public bool isListeningForControllerAssignment = false;
    private readonly List<int> assignedControllerDeviceIds = new List<int>();

    public bool isListeningForEmulatorCloseHotkeys = false;
    public int expectedEmulatorCloseHotkeysCount = 0;
    public Godot.Collections.Array collectedEmulatorCloseHotkeys = new Godot.Collections.Array();
    private static readonly PackedScene settingsListEntryScene = GD.Load<PackedScene>("res://scenes/settings_list/settings_list_entry.tscn");

    public static readonly string[] StandardSdlInputs = new string[]
    {
        "FaceSouth", "FaceEast", "FaceWest", "FaceNorth",
        "LeftShoulder", "RightShoulder",
        "LeftTrigger", "RightTrigger",
        "DpadUp", "DpadDown", "DpadLeft", "DpadRight",
        "Back", "Start", "Guide",
        "LeftStick", "RightStick",
        "LeftStick_Up", "LeftStick_Down", "LeftStick_Left", "LeftStick_Right",
        "RightStick_Up", "RightStick_Down", "RightStick_Left", "RightStick_Right"
    };

    private double secondsEmulatorCloseHotkeysHeld;
    private bool emulatorCloseRequestedThisHold;

    public MainSceneInputHandler(MainScene mainScene, AppInstance appInstance)
    {
        this.mainScene = mainScene;
        this.appInstance = appInstance;
    }

    public double EmulatorCloseHoldProgress
    {
        get
        {
            float requiredSeconds = appInstance.configManager.EmulatorCloseHoldSeconds;
            return requiredSeconds <= 0.0f ? 0.0 : Mathf.Clamp(secondsEmulatorCloseHotkeysHeld / requiredSeconds, 0.0, 1.0);
        }
    }

    public void UpdateEmulatorCloseHold()
    {
        bool hotkeysAreHeld = appInstance.emulatorManager != null
            && appInstance.emulatorManager.IsEmulatorRunning
            && AreEmulatorCloseHotkeysHeld();

        ReportCloseHotkeyHoldChange(hotkeysAreHeld);

        if (!hotkeysAreHeld)
        {
            secondsEmulatorCloseHotkeysHeld = 0.0;
            emulatorCloseRequestedThisHold = false;
            return;
        }

        framesSeenThisHold++;

        if (emulatorCloseRequestedThisHold)
        {
            return;
        }

        secondsEmulatorCloseHotkeysHeld = (Time.GetTicksMsec() - holdStartedAtMilliseconds) / 1000.0;

        if (secondsEmulatorCloseHotkeysHeld < appInstance.configManager.EmulatorCloseHoldSeconds)
        {
            return;
        }

        emulatorCloseRequestedThisHold = true;
        GD.Print($"[Input] emulator close hotkeys held for {appInstance.configManager.EmulatorCloseHoldSeconds}s ({DescribeHoldTiming()}); closing the emulator.");
        appInstance.emulatorManager.CloseEmulator();
    }

    private bool wasHoldingEmulatorCloseHotkeys;
    private ulong holdStartedAtMilliseconds;
    private int framesSeenThisHold;

    private string DescribeHoldTiming()
    {
        return $"{framesSeenThisHold} frames";
    }

    private void ReportCloseHotkeyHoldChange(bool hotkeysAreHeld)
    {
        if (hotkeysAreHeld == wasHoldingEmulatorCloseHotkeys)
        {
            return;
        }

        wasHoldingEmulatorCloseHotkeys = hotkeysAreHeld;

        if (hotkeysAreHeld)
        {
            holdStartedAtMilliseconds = Time.GetTicksMsec();
            framesSeenThisHold = 0;
            GD.Print($"[Input] emulator close hotkeys are down on device {ResolveCloseHotkeyDeviceId()}; hold for {appInstance.configManager.EmulatorCloseHoldSeconds:0.#}s to close.");
            return;
        }

        if (!emulatorCloseRequestedThisHold && secondsEmulatorCloseHotkeysHeld > 0.0)
        {
            GD.Print($"[Input] emulator close hotkeys released after {secondsEmulatorCloseHotkeysHeld:0.#}s ({DescribeHoldTiming()}); not closing.");
        }
    }

    private bool AreEmulatorCloseHotkeysHeld()
    {
        int hotkeyCount = appInstance.configManager.EmulatorCloseHotkeyCount;
        var hotkeyButtons = appInstance.configManager.EmulatorCloseHotkeys;

        if (hotkeyCount <= 0 || hotkeyButtons == null || hotkeyButtons.Count == 0)
        {
            return false;
        }

        int physicalDeviceId = ResolveCloseHotkeyDeviceId();

        if (physicalDeviceId < 0)
        {
            return false;
        }

        for (int hotkeyIndex = 0; hotkeyIndex < hotkeyCount && hotkeyIndex < hotkeyButtons.Count; hotkeyIndex++)
        {
            if (!Input.IsJoyButtonPressed(physicalDeviceId, (JoyButton)hotkeyButtons[hotkeyIndex].AsInt32()))
            {
                return false;
            }
        }

        return true;
    }

    private int ResolveCloseHotkeyDeviceId()
    {
        int layerDeviceId = appInstance.inputLayer?.ResolvePlayerOneDeviceId() ?? -1;

        if (layerDeviceId >= 0)
        {
            return layerDeviceId;
        }

        var connectedControllers = appInstance.controllerManager?.GetConnectedControllers();
        return connectedControllers != null && connectedControllers.Count > 0 ? connectedControllers[0].GodotDeviceId : -1;
    }

    private static string BuildControllerSetupOfferBody()
    {
        var body = new System.Text.StringBuilder();
        body.Append("[b]How should RomBox set up your controllers?[/b]\n\n");
        body.Append("RomBox can point each emulator at the right controller for every player, so Player 1 stays Player 1. It does this through Steam Input.\n\n");

        foreach (ControllerSetupMethod method in ControllerSetupMethod.All)
        {
            string recommendation = method == ControllerSetupMethod.Recommended ? " (Recommended)" : "";
            body.Append($"• [b]{method.Title}{recommendation}[/b]: {method.Description}\n");
        }

        if (!ControllerSetupMethod.IsLaunchedThroughSteam)
        {
            body.Append("\nRomBox is running outside Steam right now. Choosing Steam Input adds RomBox to your Steam library and restarts it from there.\n");
        }

        body.Append("\nYou can change this at any time in Settings > Input Settings.");
        return body.ToString();
    }

    private static string DescribeSetupChoice(ControllerSetupMethod method)
    {
        string title = method == ControllerSetupMethod.SteamInput && !ControllerSetupMethod.IsLaunchedThroughSteam && SteamShortcuts.IsSupportedPlatform
            ? "Add to Steam and Use Steam Input"
            : method.Title;
        return method == ControllerSetupMethod.Recommended ? $"{title} (Recommended)" : title;
    }

    public void OfferControllerLayerIfNotYetAsked()
    {
        if (appInstance.configManager.ControllerMappingConsent != ConfigManager.ControllerMappingConsentUnasked)
        {
            return;
        }

        if (!OperatingSystem.IsWindows() || OS.HasFeature("android"))
        {
            return;
        }

        if (mainScene.changelogPanel == null || mainScene.panelStack.HasOpenPanel)
        {
            return;
        }

        mainScene.changelogPanel.ShowChoices(
            ChangelogPanel.PromptSubject.ControllerLayer,
            BuildControllerSetupOfferBody(),
            ControllerSetupMethod.All.Select(DescribeSetupChoice).ToList(),
            ControllerSetupMethod.IndexOf(ControllerSetupMethod.Recommended));
    }

    public void OnControllerSetupMethodChosen(ControllerSetupMethod method)
    {
        appInstance.configManager.SaveControllerMappingConsent(method.ConsentValue);
        GD.Print($"[InputLayer] the user chose {method.Title} for controller setup.");
        UpdateControllerAssignmentPrompt();

        if (method == ControllerSetupMethod.SteamInput && !ControllerSetupMethod.IsLaunchedThroughSteam && SteamShortcuts.IsSupportedPlatform)
        {
            mainScene.SteamHandler.StartThroughSteam();
        }
    }

    public void BeginControllerAssignment()
    {
        assignedControllerDeviceIds.Clear();
        isListeningForControllerAssignment = true;
        appInstance.inputLayer?.ClearPlayerAssignment();
        UpdateControllerAssignmentPrompt();
    }

    public void RecordControllerAssignment(int deviceId)
    {
        if (assignedControllerDeviceIds.Contains(deviceId))
        {
            return;
        }

        assignedControllerDeviceIds.Add(deviceId);

        if (assignedControllerDeviceIds.Count >= CountAssignableControllers())
        {
            FinishControllerAssignment();
            return;
        }

        UpdateControllerAssignmentPrompt();
    }

    public void CancelControllerAssignment()
    {
        if (assignedControllerDeviceIds.Count > 0)
        {
            FinishControllerAssignment();
            return;
        }

        isListeningForControllerAssignment = false;
        UpdateControllerAssignmentPrompt();
    }

    private void FinishControllerAssignment()
    {
        isListeningForControllerAssignment = false;
        appInstance.inputLayer?.SetPlayerAssignment(assignedControllerDeviceIds);
        UpdateControllerAssignmentPrompt();
    }

    private int CountAssignableControllers()
    {
        var connectedControllers = appInstance.controllerManager?.GetConnectedControllers();
        return connectedControllers?.Count ?? 0;
    }

    public void UpdateControllerAssignmentPrompt()
    {
        if (mainScene.assignControllersButton == null)
        {
            return;
        }

        mainScene.assignControllersButton.Visible = appInstance.inputLayer?.CanAssignPlayers == true;

        if (isListeningForControllerAssignment)
        {
            int nextPlayerNumber = assignedControllerDeviceIds.Count + 1;
            mainScene.assignControllersButton.Text = $"Player {nextPlayerNumber}: Press a Button";
            return;
        }

        var assignedOrder = appInstance.inputLayer?.PlayerAssignmentDeviceIds;

        mainScene.assignControllersButton.Text = assignedOrder == null || assignedOrder.Count == 0
            ? "Assign Controllers"
            : $"Assign Controllers ({assignedOrder.Count} Set)";
    }

    public void UpdateEmulatorCloseHotkeysBtnText()
    {
        if (mainScene.emulatorCloseHotkeysBtn != null)
        {
            var currentKeys = appInstance.configManager.EmulatorCloseHotkeys;
            var keyNames = new List<string>();
            for (int i = 0; i < appInstance.configManager.EmulatorCloseHotkeyCount; i++)
            {
                if (i < currentKeys.Count)
                {
                    keyNames.Add(((JoyButton)currentKeys[i].AsInt32()).ToString());
                }
            }
            mainScene.emulatorCloseHotkeysBtn.Text = $"Record Hotkeys [Hold {string.Join(" + ", keyNames)} for {appInstance.configManager.EmulatorCloseHoldSeconds:0.#}s]";
        }
    }

    public string ConvertInputEventToStandardString(InputEvent @event)
    {
        if (@event is InputEventJoypadButton joyBtn)
        {
            switch (joyBtn.ButtonIndex)
            {
                case JoyButton.A: return "FaceSouth";
                case JoyButton.B: return "FaceEast";
                case JoyButton.X: return "FaceWest";
                case JoyButton.Y: return "FaceNorth";
                case JoyButton.LeftShoulder: return "LeftShoulder";
                case JoyButton.RightShoulder: return "RightShoulder";
                case JoyButton.Back: return "Back";
                case JoyButton.Start: return "Start";
                case JoyButton.Guide: return "Guide";
                case JoyButton.LeftStick: return "LeftStick";
                case JoyButton.RightStick: return "RightStick";
                case JoyButton.DpadUp: return "DpadUp";
                case JoyButton.DpadDown: return "DpadDown";
                case JoyButton.DpadLeft: return "DpadLeft";
                case JoyButton.DpadRight: return "DpadRight";
            }
        }
        else if (@event is InputEventJoypadMotion joyMotion && Mathf.Abs(joyMotion.AxisValue) > 0.5f)
        {
            bool isPositive = joyMotion.AxisValue > 0;
            switch (joyMotion.Axis)
            {
                case JoyAxis.TriggerLeft: return "LeftTrigger";
                case JoyAxis.TriggerRight: return "RightTrigger";
                case JoyAxis.LeftX: return isPositive ? "LeftStick_Right" : "LeftStick_Left";
                case JoyAxis.LeftY: return isPositive ? "LeftStick_Down" : "LeftStick_Up";
                case JoyAxis.RightX: return isPositive ? "RightStick_Right" : "RightStick_Left";
                case JoyAxis.RightY: return isPositive ? "RightStick_Down" : "RightStick_Up";
            }
        }
        else if (@event is InputEventKey keyEvent)
        {
            return "Key_" + keyEvent.Keycode.ToString();
        }
        return null;
    }

    public void GeneratePlatformControllerMappingsUI(string systemSlug, EmulatorMeta meta, Control parentContainer)
    {
        foreach (Node child in parentContainer.GetChildren())
        {
            child.QueueFree();
        }

        if (meta.ControllerConfig == null || meta.ControllerConfig.PlatformLayout == null || meta.ControllerConfig.PlatformLayout.Count == 0)
        {
            return;
        }

        VBoxContainer vbox = new VBoxContainer();
        parentContainer.AddChild(vbox);

        vbox.AddChild(new HSeparator());

        Label title = new Label();
        title.Text = "Controller Mappings";
        title.ThemeTypeVariation = "MutedLabel";
        vbox.AddChild(title);

        int maxControllers = meta.ControllerConfig.MaxControllers > 0 ? meta.ControllerConfig.MaxControllers : 1;

        var allMappings = appInstance.configManager.PlatformInputMappings.ContainsKey(systemSlug)
            ? appInstance.configManager.PlatformInputMappings[systemSlug]
            : new Dictionary<int, Dictionary<string, string>>();

        for (int playerIndex = 0; playerIndex < maxControllers; playerIndex++)
        {
            int currentPlayerIndex = playerIndex;
            var currentMappings = allMappings.ContainsKey(currentPlayerIndex) ? allMappings[currentPlayerIndex] : new Dictionary<string, string>();

            VBoxContainer playerBox = new VBoxContainer();
            vbox.AddChild(playerBox);

            Label playerLabel = new Label();
            playerLabel.Text = $"Player {currentPlayerIndex + 1}";
            playerLabel.ThemeTypeVariation = "AccentLabel";
            playerBox.AddChild(playerLabel);

            foreach (var kvp in meta.ControllerConfig.PlatformLayout)
            {
                string buttonName = kvp.Key;
                string defaultSdl = kvp.Value;

                HBoxContainer row = new HBoxContainer();
                Label lbl = new Label();
                lbl.Text = buttonName;
                lbl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(lbl);

                Button mappingBtn = new Button();
                mappingBtn.ThemeTypeVariation = "ListButton";
                string mappedInput = currentMappings.ContainsKey(buttonName) ? currentMappings[buttonName] : defaultSdl;
                mappingBtn.Text = string.IsNullOrEmpty(mappedInput) ? "Unmapped" : mappedInput;

                mappingBtn.Pressed += () =>
                {
                    mappingBtn.Text = "Listening...";
                    isListeningForInput = true;
                    inputListenCallback = (string detectedInput) =>
                    {
                        mappingBtn.Text = detectedInput;
                        appInstance.configManager.SavePlatformInputMapping(systemSlug, currentPlayerIndex, buttonName, detectedInput);
                        mappingBtn.GrabFocus();
                    };
                };

                row.AddChild(mappingBtn);

                var entry = settingsListEntryScene.Instantiate<MarginContainer>();
                entry.AddChild(row);
                playerBox.AddChild(entry);
            }

            if (playerIndex < maxControllers - 1)
            {
                playerBox.AddChild(new HSeparator());
            }
        }
    }
}
