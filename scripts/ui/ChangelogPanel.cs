using Godot;
using System.Collections.Generic;

[GlobalClass]
public partial class ChangelogPanel : UiPanel
{
    [Export] public RichTextLabel notesLabel;
    [Export] public Button acceptButton;
    [Export] public Button cancelButton;

    [Signal]
    public delegate void AcceptedEventHandler();

    [Signal]
    public delegate void DismissedEventHandler();

    public override void _Ready()
    {
        base._Ready();

        if (notesLabel != null)
        {
            notesLabel.BbcodeEnabled = true;
        }

        if (acceptButton != null)
        {
            acceptButton.Pressed += EmitAccepted;
            ControllerGlyph.Apply(acceptButton, "Select");
        }

        if (cancelButton != null)
        {
            cancelButton.Pressed += EmitDismissed;
            ControllerGlyph.Apply(cancelButton, "Back");
        }
    }

    public enum PromptSubject
    {
        ApplicationUpdate,
        ControllerLayer,
        GameFolderAccess,
        AndroidCoreSetup,
        AndroidExitHint,
        SteamRestart,
        SteamShortcutOutcome,
        SteamLaunchOffer
    }

    public PromptSubject ActiveSubject { get; private set; } = PromptSubject.ApplicationUpdate;

    // goesBackToFullRelease: the player turned test builds off while on one, and version is the
    // latest full release, older than what is installed.
    public void ShowUpdate(string version, string releaseNotes, bool goesBackToFullRelease = false)
    {
        ActiveSubject = PromptSubject.ApplicationUpdate;

        if (notesLabel != null)
        {
            string sanitizedNotes = EscapeMarkup(releaseNotes.Replace("\r", "").Replace("\b", ""));
            notesLabel.Text = goesBackToFullRelease
                ? $"[b]Go back to version {version}?[/b]\n\nYou turned off test builds, and this copy of RomBox is one. Installing puts back the latest finished release. Your games, saves and settings stay as they are."
                : $"[b]A new version ({version}) of RomBox is available.[/b]\n\nRelease Notes:\n{sanitizedNotes}";
        }

        if (acceptButton != null) acceptButton.Text = "Install";
        if (cancelButton != null) cancelButton.Text = "Close";

        Open();
    }

    public void ShowPrompt(PromptSubject subject, string bodyText, string acceptText, string declineText)
    {
        ActiveSubject = subject;
        RemoveChoices();

        if (notesLabel != null)
        {
            notesLabel.Text = bodyText;
        }

        if (acceptButton != null) acceptButton.Text = acceptText;
        if (cancelButton != null) cancelButton.Text = declineText;

        Open();
    }

    [Signal]
    public delegate void ChoiceMadeEventHandler(int choiceIndex);

    private VBoxContainer choiceList;

    public bool IsShowingChoices => choiceList != null;

    public void ShowChoices(PromptSubject subject, string bodyText, IReadOnlyList<string> choiceTexts, int focusedChoiceIndex)
    {
        ActiveSubject = subject;
        RemoveChoices();

        if (notesLabel != null)
        {
            notesLabel.Text = bodyText;
        }

        Control buttonRow = acceptButton?.GetParent<Control>();
        Node promptLayout = buttonRow?.GetParent();

        if (promptLayout == null)
        {
            return;
        }

        choiceList = new VBoxContainer();
        choiceList.AddThemeConstantOverride("separation", 5);

        for (int choiceIndex = 0; choiceIndex < choiceTexts.Count; choiceIndex++)
        {
            int pressedChoiceIndex = choiceIndex;
            var choiceButton = new Button();
            choiceButton.ThemeTypeVariation = "ListButton";
            choiceButton.Text = choiceTexts[choiceIndex];
            choiceButton.Pressed += () => EmitSignal(SignalName.ChoiceMade, pressedChoiceIndex);
            choiceList.AddChild(choiceButton);
        }

        promptLayout.AddChild(choiceList);
        promptLayout.MoveChild(choiceList, buttonRow.GetIndex());
        buttonRow.Visible = false;

        Open();

        Button focusedChoice = choiceList.GetChild<Button>(System.Math.Clamp(focusedChoiceIndex, 0, choiceTexts.Count - 1));
        focusedChoice.CallDeferred(Control.MethodName.GrabFocus);
    }

    private void RemoveChoices()
    {
        if (choiceList == null)
        {
            return;
        }

        choiceList.QueueFree();
        choiceList = null;

        if (acceptButton?.GetParent() is Control buttonRow)
        {
            buttonRow.Visible = true;
        }
    }

    public override bool HandleInput(InputEvent inputEvent)
    {
        if (State == PanelState.Closing) return true;

        if (IsShowingChoices)
        {
            return HandleChoiceInput(inputEvent);
        }

        if (inputEvent.IsActionPressed("ui_accept") || inputEvent.IsActionPressed("Select"))
        {
            EmitAccepted();
        }
        else if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            EmitDismissed();
        }

        return true;
    }

    private bool HandleChoiceInput(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("ui_up", true) || inputEvent.IsActionPressed("MoveUp"))
        {
            FocusCycler.Cycle(choiceList, -1);
        }
        else if (inputEvent.IsActionPressed("ui_down", true) || inputEvent.IsActionPressed("MoveDown"))
        {
            FocusCycler.Cycle(choiceList, 1);
        }
        else if (inputEvent.IsActionPressed("ui_accept") || inputEvent.IsActionPressed("Select"))
        {
            if (GetViewport().GuiGetFocusOwner() is Button focusedChoice && focusedChoice.GetParent() == choiceList)
            {
                EmitSignal(SignalName.ChoiceMade, focusedChoice.GetIndex());
            }
        }
        else if (inputEvent.IsActionPressed("ui_cancel") || inputEvent.IsActionPressed("Back"))
        {
            EmitDismissed();
        }

        return true;
    }

    private static string EscapeMarkup(string untrustedText)
    {
        return untrustedText.Replace("[", "[lb]");
    }

    private void EmitAccepted()
    {
        EmitSignal(SignalName.Accepted);
    }

    private void EmitDismissed()
    {
        EmitSignal(SignalName.Dismissed);
    }
}
