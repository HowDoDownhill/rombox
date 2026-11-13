using Godot;
using System;

public class MainSceneUpdaterHandler
{
    private MainScene mainScene;
    private AppInstance appInstance;
    private AppUpdater appUpdater;
    private string pendingUpdateVersion;

    public MainSceneUpdaterHandler(MainScene mainScene, AppInstance appInstance)
    {
        this.mainScene = mainScene;
        this.appInstance = appInstance;
    }

    public void InitUpdater()
    {
        appUpdater = mainScene.GetNodeOrNull<AppUpdater>("/root/AppUpdater");

        if (appUpdater != null)
        {
            appUpdater.UpdateAvailable += OnUpdateAvailable;
            appUpdater.UpdateDownloadProgress += OnUpdateDownloadProgress;
            appUpdater.UpdateDownloadCompleted += OnUpdateDownloadCompleted;

            _ = appUpdater.CheckForUpdatesAsync();
        }
    }

    public void Detach()
    {
        if (appUpdater == null)
        {
            return;
        }

        appUpdater.UpdateAvailable -= OnUpdateAvailable;
        appUpdater.UpdateDownloadProgress -= OnUpdateDownloadProgress;
        appUpdater.UpdateDownloadCompleted -= OnUpdateDownloadCompleted;
    }

    private void OnUpdateAvailable(string version, string releaseNotes, bool goesBackToFullRelease)
    {
        pendingUpdateVersion = version;
        pendingUpdateGoesBack = goesBackToFullRelease;

        mainScene.changelogPanel?.ShowUpdate(version, releaseNotes, goesBackToFullRelease);
    }

    private void OnUpdateDownloadProgress(float progress)
    {
        mainScene.progressPanel?.SetProgress(progress * 100.0f);
    }

    private async void OnUpdateDownloadCompleted(bool success)
    {
        if (success)
        {
            mainScene.progressPanel?.SetStatus("Download complete. Restarting to apply update...");

            await mainScene.ToSignal(mainScene.GetTree().CreateTimer(3.0f), "timeout");
            appUpdater.ApplyUpdateAndRestart();
        }
        else
        {
            mainScene.progressPanel?.SetStatus("Failed to download update. Please try again later.");

            await mainScene.ToSignal(mainScene.GetTree().CreateTimer(3.0f), "timeout");

            mainScene.progressPanel?.Close();
        }
    }

    public void OnAcceptUpdatePressed()
    {
        mainScene.changelogPanel?.Close();

        if (!string.IsNullOrEmpty(pendingUpdateVersion))
        {
            mainScene.progressPanel?.ShowStatus("Downloading...");

            _ = appUpdater.DownloadUpdateAsync(pendingUpdateVersion);
        }
    }

    // Set while the offer on screen is to go back from a test build to the latest full release.
    private bool pendingUpdateGoesBack;

    public void OnCancelUpdatePressed()
    {
        mainScene.changelogPanel?.Close();

        // The offer to go back is made once per test build. Turning the test builds switch on
        // and off again brings it back.
        if (pendingUpdateGoesBack)
        {
            pendingUpdateGoesBack = false;
            appInstance?.configManager?.SaveDeclinedGoBackFrom(AppUpdater.CurrentVersion);
        }
    }
}
