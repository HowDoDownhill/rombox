using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Offers to add RomBox to Steam and start it from there, and does the work when the user agrees:
// close Steam if a shortcut has to be written, write it with artwork and Steam Input turned on,
// then start the shortcut through Steam and quit this copy.
public class MainSceneSteamHandler
{
    private const float StatusVisibleSeconds = 2.0f;

    private const int AcceptChoice = 0;
    public const int NotNowChoice = 1;
    private const int NeverChoice = 2;

    private readonly MainScene mainScene;
    private readonly AppInstance appInstance;

    public event Action ShortcutChanged;

    public MainSceneSteamHandler(MainScene mainScene, AppInstance appInstance)
    {
        this.mainScene = mainScene;
        this.appInstance = appInstance;
    }

    private const string Benefits =
        "• [b]Steam Input.[/b] Every controller works, and each emulator gets the right one for every player.\n" +
        "• [b]Steam Deck Game Mode and Big Picture.[/b]\n" +
        "• [b]The Steam overlay, Remote Play and Steam Link.[/b]\n";

    private const string WhatYouMiss =
        "If you skip this, your games still work. You set up controllers in each emulator yourself, and RomBox stays out of Game Mode, the overlay and streaming.";

    public static string BuildOfferBody(SteamShortcutState state)
    {
        string heading = state switch
        {
            SteamShortcutState.Current => "[b]Start RomBox through Steam?[/b]\n\nRomBox is in your Steam library. Starting it from there gives you:\n",
            SteamShortcutState.Outdated => "[b]Fix RomBox's Steam shortcut?[/b]\n\nYour Steam shortcut points at an old location or name. Fixed, and started from Steam, you get:\n",
            _ => "[b]Play RomBox through Steam?[/b]\n\nRomBox can add itself to your Steam library and restart from there. You get:\n"
        };

        string howItWorks = state == SteamShortcutState.Current
            ? "RomBox closes and Steam starts it again."
            : "Steam restarts if it is open, then starts RomBox.";

        return heading + Benefits + "\n" + WhatYouMiss + "\n\n" + howItWorks;
    }

    private static IReadOnlyList<string> OfferChoices(SteamShortcutState state)
    {
        string accept = state switch
        {
            SteamShortcutState.Current => "Restart Through Steam (Recommended)",
            SteamShortcutState.Outdated => "Fix and Restart Through Steam (Recommended)",
            _ => "Add to Steam and Restart (Recommended)"
        };

        return new[] { accept, "Not Now", "Don't Ask Again" };
    }

    // Shows the offer when it can help: a desktop build started outside Steam, with Steam installed.
    // Returns whether it was shown, so the next startup prompt can wait for it.
    public bool OfferIfUseful()
    {
        if (!SteamShortcuts.IsSupportedPlatform || SteamShortcuts.IsLaunchedThroughSteam || !appInstance.configManager.OfferSteamLaunch)
        {
            return false;
        }

        if (mainScene.changelogPanel == null || mainScene.panelStack.HasOpenPanel)
        {
            return false;
        }

        SteamShortcutState state = SteamShortcuts.GetState();

        if (state == SteamShortcutState.SteamNotFound)
        {
            return false;
        }

        mainScene.changelogPanel.ShowChoices(ChangelogPanel.PromptSubject.SteamLaunchOffer, BuildOfferBody(state), OfferChoices(state), AcceptChoice);
        return true;
    }

    public void OnOfferChoiceMade(int choiceIndex)
    {
        if (choiceIndex == AcceptChoice)
        {
            StartThroughSteam();
            return;
        }

        if (choiceIndex == NeverChoice)
        {
            appInstance.configManager.SaveOfferSteamLaunch(false);
            GD.Print("[Steam] the user chose not to be asked about starting through Steam again.");
        }

        mainScene.InputHandler.OfferControllerLayerIfNotYetAsked();
    }

    // Adds or repairs the shortcut as needed, then restarts this app through Steam.
    public async void StartThroughSteam()
    {
        if (!SteamShortcuts.IsSupportedPlatform)
        {
            return;
        }

        if (appInstance.downloadManager?.HasUnfinishedTransfers == true)
        {
            ShowOutcome("[b]Downloads are still running.[/b]\n\nRomBox restarts to move into Steam. Let your downloads finish, then choose Add to Steam in Settings > Input Settings.");
            return;
        }

        SteamShortcutState state = SteamShortcuts.GetState();

        if (state == SteamShortcutState.SteamNotFound)
        {
            ShowOutcome("[b]Steam wasn't found.[/b]\n\nInstall Steam and sign in, then choose Add to Steam in Settings > Input Settings.");
            return;
        }

        if (state == SteamShortcutState.Current)
        {
            // Artwork and Steam Input can only be written while Steam is closed.
            if (!SteamShortcuts.IsSteamRunning())
            {
                SteamShortcuts.AddOrUpdateThisApp(ShouldEnableSteamInput());
            }

            await RelaunchThroughSteam();
            return;
        }

        if (SteamShortcuts.IsSteamRunning())
        {
            mainScene.changelogPanel?.ShowPrompt(
                ChangelogPanel.PromptSubject.SteamRestart,
                "[b]Restart Steam?[/b]\n\nSteam only reads new library entries when it starts. RomBox will close Steam, add itself, and start again from Steam.",
                "Restart Steam",
                "Not Now");
            return;
        }

        await AddAndRelaunch();
    }

    public async void RestartSteamThenAddAndRelaunch()
    {
        mainScene.progressPanel?.ShowStatus("Closing Steam...");
        await SteamShortcuts.CloseSteam();

        if (SteamShortcuts.IsSteamRunning())
        {
            mainScene.progressPanel?.Close();
            ShowOutcome("[b]Steam didn't close.[/b]\n\nClose Steam yourself, then choose Add to Steam in Settings > Input Settings.");
            return;
        }

        await AddAndRelaunch();
    }

    private bool ShouldEnableSteamInput()
    {
        return appInstance.configManager.ControllerMappingConsent != ConfigManager.ControllerMappingConsentDeclined;
    }

    private async Task AddAndRelaunch()
    {
        mainScene.progressPanel?.ShowStatus("Adding RomBox to Steam...");
        SteamShortcutResult result = SteamShortcuts.AddOrUpdateThisApp(ShouldEnableSteamInput());
        ShortcutChanged?.Invoke();

        if (result == SteamShortcutResult.SteamNotFound || result == SteamShortcutResult.Failed)
        {
            mainScene.progressPanel?.Close();
            ShowOutcome(result == SteamShortcutResult.SteamNotFound
                ? "[b]Steam wasn't found.[/b]\n\nInstall Steam and sign in, then choose Add to Steam in Settings > Input Settings."
                : "[b]RomBox couldn't add itself to Steam.[/b]\n\nAdd it yourself: in Steam, choose Games > Add a Non-Steam Game to My Library and pick this app. Then open its Properties > Controller and choose Enable Steam Input.");
            SteamShortcuts.OpenSteamLibrary();
            return;
        }

        await RelaunchThroughSteam();
    }

    private async Task RelaunchThroughSteam()
    {
        mainScene.progressPanel?.ShowStatus("Starting RomBox through Steam...");
        SteamShortcuts.LaunchThroughSteam();
        await mainScene.ToSignal(mainScene.GetTree().CreateTimer(StatusVisibleSeconds), "timeout");
        GD.Print("[Steam] quitting so Steam's copy takes over.");
        mainScene.GetTree().Quit();
    }

    private void ShowOutcome(string body)
    {
        mainScene.changelogPanel?.ShowPrompt(ChangelogPanel.PromptSubject.SteamShortcutOutcome, body, "OK", "Close");
    }

    public static string DescribeLibraryStatus()
    {
        if (SteamShortcuts.IsLaunchedThroughSteam)
        {
            return "Running Through Steam";
        }

        return SteamShortcuts.GetState() switch
        {
            SteamShortcutState.Current => "In Your Steam Library",
            SteamShortcutState.Outdated => "Steam Shortcut Out of Date",
            SteamShortcutState.SteamNotFound => "Steam Not Found",
            _ => "Not in Your Steam Library"
        };
    }

    public static string DescribeLibraryAction()
    {
        return SteamShortcuts.GetState() switch
        {
            SteamShortcutState.Current => "Restart Through Steam",
            SteamShortcutState.Outdated => "Fix Steam Shortcut",
            _ => "Add to Steam"
        };
    }
}
