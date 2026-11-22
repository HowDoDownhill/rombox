using Godot;

[GlobalClass]
public partial class StartMenuPanel : UiPanel
{
    [Export] public Control menuView;
    [Export] public Control biosView;
    [Export] public Control optionsList;
    [Export] public VBoxContainer biosList;

    [ExportGroup("Options")]
    [Export] public Button launchEmulatorButton;
    [Export] public Button updateEmulatorButton;
    [Export] public Button uninstallEmulatorButton;
    [Export] public Button selectBiosButton;
    [Export] public Button favoriteGameButton;
    [Export] public Button hostNetplayButton;
    [Export] public Button joinNetplayButton;
    [Export] public Control netplayView;
    [Export] public Label netplayCodeLabel;
    [Export] public Label netplayInfoLabel;
    [Export] public Button assignControllersButton;
    [Export] public Button settingsButton;
    [Export] public Button randomGameButton;
    [Export] public Button refreshAllGamesButton;
    [Export] public Button refreshCurrentSystemButton;
    [Export] public Button openRomsFolderButton;
    [Export] public Button openRecompLauncherButton;
    [Export] public Button getRecompButton;
    [Export] public Button quitButton;

    private Control focusBeforeBiosView;

    [Signal]
    public delegate void BiosViewRequestedEventHandler();

    [Signal]
    public delegate void NetplayCancelRequestedEventHandler();

    [Signal]
    public delegate void JoinCodeEnteredEventHandler(string joinCode);

    // Typing a host's code. The code label shows what has been entered, one place at a time.
    // The panel reads the keys itself: while a panel is open, text fields receive no key presses.
    private const char EmptyCodePlace = '\0';
    private readonly char[] enteredCode = new char[NetplayManager.JoinCodeLength];
    private int codeCursor;
    private bool isEnteringCode;
    private Control codeEntryButtons;

    public bool IsBiosViewOpen => biosView != null && biosView.Visible;

    // The menu is a panel against the right edge of the window, as tall as the window.
    private const float MinimumPanelWidth = 460.0f;
    private const float SlideDistance = 48.0f;
    private const float DimBehindMenu = 0.35f;
    private const int HeadingInset = 18;
    private const int HeadingGap = 14;
    private const float FocusedOptionMargin = 64.0f;

    private ColorRect backdrop;
    private Tween slideTween;

    public override void _Ready()
    {
        base._Ready();
        CloseOnOutsidePress = true;
        Transition = PanelTransition.Fade;
        AddBackdrop();
        StyleOptions();
        KeepFocusedOptionInView();
        AboutToOpen += SlideIn;
    }

    private void AddBackdrop()
    {
        backdrop = new ColorRect { Color = new Color(0, 0, 0, DimBehindMenu), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(backdrop);
        MoveChild(backdrop, 0);
    }

    // The focused option keeps a margin from the top and bottom of the list, wide enough that
    // the first option of a group brings its heading into view with it.
    private void KeepFocusedOptionInView()
    {
        if (optionsList?.GetParent() is ScrollContainer scroll)
        {
            FocusScroller.KeepVisible(scroll, optionsList, FocusedOptionMargin, vertical: true);
        }
    }

    // Options read from the left. Each heading stands a little apart from the group above it,
    // and shows only while one of its group's options does.
    private void StyleOptions()
    {
        if (optionsList == null) return;

        bool isFirstHeading = true;

        foreach (Node child in optionsList.GetChildren())
        {
            if (child is Button option)
            {
                option.Alignment = HorizontalAlignment.Left;
                option.VisibilityChanged += ShowHeadingsInUse;
            }
            else if (child is Label heading)
            {
                var spaced = new StyleBoxEmpty { ContentMarginLeft = HeadingInset, ContentMarginTop = isFirstHeading ? 0 : HeadingGap };
                heading.AddThemeStyleboxOverride("normal", spaced);
                isFirstHeading = false;
            }
        }

        ShowHeadingsInUse();
    }

    private void ShowHeadingsInUse()
    {
        if (optionsList == null) return;

        Label heading = null;

        foreach (Node child in optionsList.GetChildren())
        {
            if (child is Label nextHeading)
            {
                heading = nextHeading;
                heading.Visible = false;
            }
            else if (child is Button { Visible: true } && heading != null)
            {
                heading.Visible = true;
            }
        }
    }

    private void FitPanelToMenu()
    {
        ShowHeadingsInUse();

        float contentWidth = menuView != null ? menuView.GetCombinedMinimumSize().X : 0.0f;
        float panelWidth = Mathf.Max(MinimumPanelWidth, contentWidth);

        OffsetLeft = -panelWidth;
        OffsetRight = 0;
        OffsetTop = 0;
        OffsetBottom = 0;

        // The dim covers the whole window, with room to spare for the slide.
        Vector2 windowSize = GetViewportRect().Size;
        backdrop.Position = new Vector2(panelWidth - windowSize.X - SlideDistance, 0);
        backdrop.Size = new Vector2(windowSize.X + SlideDistance * 2, windowSize.Y);
    }

    private void SlideIn()
    {
        FitPanelToMenu();

        float restingLeft = OffsetLeft;
        OffsetLeft += SlideDistance;
        OffsetRight += SlideDistance;

        slideTween?.Kill();
        slideTween = CreateTween().SetParallel(true).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        slideTween.TweenProperty(this, "offset_left", restingLeft, OpenDuration);
        slideTween.TweenProperty(this, "offset_right", 0.0f, OpenDuration);
    }

    public void ShowMenuView()
    {
        if (!IsOpen) FitPanelToMenu();
        if (menuView != null) menuView.Visible = true;
        if (biosView != null) biosView.Visible = false;
        if (netplayView != null) netplayView.Visible = false;

        if (GodotObject.IsInstanceValid(focusBeforeBiosView))
        {
            focusBeforeBiosView.GrabFocus();
        }
        else
        {
            FocusCycler.Cycle(optionsList, 0);
        }

        focusBeforeBiosView = null;
    }

    public void ShowBiosView()
    {
        focusBeforeBiosView = GetViewport().GuiGetFocusOwner();

        if (menuView != null) menuView.Visible = false;
        if (biosView != null) biosView.Visible = true;
        if (netplayView != null) netplayView.Visible = false;

        EmitSignal(SignalName.BiosViewRequested);
    }

    public void ShowNetplayView(string joinCode, string joinInformation)
    {
        focusBeforeBiosView = GetViewport().GuiGetFocusOwner();

        if (menuView != null) menuView.Visible = false;
        if (biosView != null) biosView.Visible = false;
        if (netplayView != null) netplayView.Visible = true;

        if (netplayCodeLabel != null) netplayCodeLabel.Text = joinCode;
        if (netplayInfoLabel != null) netplayInfoLabel.Text = joinInformation;

        isEnteringCode = false;
        if (codeEntryButtons != null) codeEntryButtons.Visible = false;
    }

    // Shows the join view with the code open for typing. startingCode fills the first places.
    public void ShowJoinCodeEntry(string startingCode, string information)
    {
        ShowNetplayView("", information);
        isEnteringCode = true;
        AddCodeEntryButtons();
        if (codeEntryButtons != null) codeEntryButtons.Visible = true;
        System.Array.Fill(enteredCode, EmptyCodePlace);
        codeCursor = 0;
        TypeIntoCode(startingCode);
    }

    private void AddCodeEntryButtons()
    {
        if (codeEntryButtons != null || netplayInfoLabel?.GetParent() is not Control view)
        {
            return;
        }

        var row = new HBoxContainer();
        row.Alignment = BoxContainer.AlignmentMode.Center;
        row.AddThemeConstantOverride("separation", 8);

        // For the mouse. Keys and controllers act on the code directly, so these take no focus.
        var pasteButton = new Button { Text = "Paste", FocusMode = FocusModeEnum.None };
        pasteButton.Pressed += PasteIntoCode;
        row.AddChild(pasteButton);

        var clearButton = new Button { Text = "Clear", FocusMode = FocusModeEnum.None };
        clearButton.Pressed += () =>
        {
            System.Array.Fill(enteredCode, EmptyCodePlace);
            codeCursor = 0;
            ShowEnteredCode();
        };
        row.AddChild(clearButton);

        var joinButton = new Button { Text = "Join", FocusMode = FocusModeEnum.None };
        joinButton.Pressed += SubmitCode;
        row.AddChild(joinButton);

        view.AddChild(row);
        codeEntryButtons = row;
    }

    private void ShowEnteredCode()
    {
        if (netplayCodeLabel == null)
        {
            return;
        }

        var shown = new System.Text.StringBuilder();

        for (int place = 0; place < enteredCode.Length; place++)
        {
            string character = enteredCode[place] == EmptyCodePlace ? "_" : enteredCode[place].ToString();
            shown.Append(place == codeCursor ? $"[{character}]" : $" {character} ");
        }

        netplayCodeLabel.Text = shown.ToString();
    }

    // Puts each usable character at the cursor and moves on. Anything a code cannot contain is
    // passed over, so a pasted sentence with a code in it still fills the places.
    private void TypeIntoCode(string text)
    {
        foreach (char character in NetplayManager.NormaliseJoinCode(text ?? ""))
        {
            if (NetplayManager.JoinCodeAlphabet.IndexOf(character) < 0)
            {
                continue;
            }

            enteredCode[codeCursor] = character;

            if (codeCursor == enteredCode.Length - 1)
            {
                break;
            }

            codeCursor++;
        }

        ShowEnteredCode();
    }

    private void PasteIntoCode()
    {
        System.Array.Fill(enteredCode, EmptyCodePlace);
        codeCursor = 0;
        TypeIntoCode(DisplayServer.ClipboardGet());
    }

    private void StepCodeCharacter(int direction)
    {
        string alphabet = NetplayManager.JoinCodeAlphabet;
        int index = alphabet.IndexOf(enteredCode[codeCursor]);
        index = index < 0 ? (direction > 0 ? 0 : alphabet.Length - 1) : (index + direction + alphabet.Length) % alphabet.Length;
        enteredCode[codeCursor] = alphabet[index];
        ShowEnteredCode();
    }

    private void SubmitCode()
    {
        if (System.Array.IndexOf(enteredCode, EmptyCodePlace) >= 0)
        {
            return;
        }

        EmitSignal(SignalName.JoinCodeEntered, new string(enteredCode));
    }

    private bool HandleCodeEntryInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventKey { Pressed: true } key)
        {
            if (key.Keycode == Key.V && (key.CtrlPressed || key.MetaPressed))
            {
                PasteIntoCode();
                return true;
            }

            if (key.Keycode == Key.Backspace || key.Keycode == Key.Delete)
            {
                // Backspace on an empty place steps back and clears the one before.
                if (key.Keycode == Key.Backspace && enteredCode[codeCursor] == EmptyCodePlace && codeCursor > 0)
                {
                    codeCursor--;
                }

                enteredCode[codeCursor] = EmptyCodePlace;
                ShowEnteredCode();
                return true;
            }

            if (key.Unicode > 32 && !key.CtrlPressed && !key.AltPressed && !key.MetaPressed)
            {
                TypeIntoCode(((char)key.Unicode).ToString());
                return true;
            }
        }

        if (inputEvent.IsActionPressed("ui_left", true))
        {
            codeCursor = System.Math.Max(codeCursor - 1, 0);
            ShowEnteredCode();
        }
        else if (inputEvent.IsActionPressed("ui_right", true))
        {
            codeCursor = System.Math.Min(codeCursor + 1, enteredCode.Length - 1);
            ShowEnteredCode();
        }
        else if (inputEvent.IsActionPressed("ui_up", true) || inputEvent.IsActionPressed("MoveUp"))
        {
            StepCodeCharacter(1);
        }
        else if (inputEvent.IsActionPressed("ui_down", true) || inputEvent.IsActionPressed("MoveDown"))
        {
            StepCodeCharacter(-1);
        }
        else if (inputEvent.IsActionPressed("ui_accept") || inputEvent.IsActionPressed("Select"))
        {
            SubmitCode();
        }
        else if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            EmitSignal(SignalName.NetplayCancelRequested);
        }

        return true;
    }

    public bool IsNetplayViewOpen => netplayView != null && netplayView.Visible;

    protected override void OnOpened()
    {
        FocusCycler.Cycle(optionsList, 0);
    }

    public override bool HandleInput(InputEvent inputEvent)
    {
        if (State == PanelState.Closing) return true;

        if (IsNetplayViewOpen && isEnteringCode)
        {
            return HandleCodeEntryInput(inputEvent);
        }

        Control activeList = optionsList;

        if (IsBiosViewOpen) activeList = biosView;
        else if (IsNetplayViewOpen) activeList = netplayView;

        if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            if (IsBiosViewOpen) ShowMenuView();
            else if (IsNetplayViewOpen) EmitSignal(SignalName.NetplayCancelRequested);
            else Close();
            return true;
        }

        if (inputEvent.IsActionPressed("ui_up", true) || inputEvent.IsActionPressed("MoveUp"))
        {
            FocusCycler.Cycle(activeList, -1);
            return true;
        }

        if (inputEvent.IsActionPressed("ui_down", true) || inputEvent.IsActionPressed("MoveDown"))
        {
            FocusCycler.Cycle(activeList, 1);
            return true;
        }

        if (inputEvent.IsActionPressed("ui_accept") || inputEvent.IsActionPressed("Select"))
        {
            if (GetViewport().GuiGetFocusOwner() is BaseButton focusedButton && !focusedButton.Disabled)
            {
                focusedButton.EmitSignal(BaseButton.SignalName.Pressed);
            }
        }

        return true;
    }
}
