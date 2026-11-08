using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

public partial class LoginScreen : Control
{
    [ExportGroup("Common Nodes")]
    [Export] private Button loginButton;
    [Export] private Label errorLabel;

    [ExportGroup("RomM Backend")]
    [Export] private LineEdit rommHostInput;
    [Export] private LineEdit rommUserInput;
    [Export] private LineEdit rommPasswordInput;
    [Export] private LineEdit rommApiKeyInput;

    [ExportGroup("Panel Shadow")]
    [Export(PropertyHint.Range, "0,120,1")] private int panelShadowSize = MicaShadow.DefaultSize;
    [Export] private Color panelShadowColor = MicaShadow.DefaultColor;
    [Export] private Vector2 panelShadowOffset = MicaShadow.DefaultOffset;

    private AppInstance appInstance; 

    public const string SetupWizardScenePath = "res://scenes/setup/setup_wizard.tscn";
    
    public override void _Ready()
    {
        StartupTimeline.Mark("login screen");
        appInstance = GetNode<AppInstance>("/root/AppInstance");
        
        loginButton.Pressed += OnLoginButtonPressed;

        // A new install sets up first. A local library has nothing to sign in to.
        if (!appInstance.configManager.SetupComplete || OS.GetCmdlineUserArgs().Any(argument => argument.StartsWith("--setup-capture=")))
        {
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, SetupWizardScenePath);
            return;
        }

        if (appInstance.Library.SourceId != LibrarySources.RomM)
        {
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, "res://scenes/login/loading_screen.tscn");
            return;
        }

        Appearance.ApplySaved(appInstance.configManager);

        var micaMaterial = GD.Load<ShaderMaterial>("res://assets/materials/mica_panel.tres");
        MicaShadow.AttachToAll(this, micaMaterial, panelShadowColor, panelShadowSize, panelShadowOffset);

        PopulateFieldsFromConfig();
        FocusFirstControl();
        AttemptAutoLogin();
        ControllerTestKeys.RunIfRequested(this);
    }

    // With nothing focused, a controller would have nothing to move from.
    private void FocusFirstControl()
    {
        Control first = string.IsNullOrEmpty(rommHostInput.Text) ? rommHostInput : loginButton;
        Callable.From(first.GrabFocus).CallDeferred();
    }

    public override void _Input(InputEvent inputEvent)
    {
        Viewport viewport = GetViewport();

        if (ControllerPress.IsPress(inputEvent) && viewport.GuiGetFocusOwner() is Control focused && ControllerPress.Press(focused))
        {
            viewport.SetInputAsHandled();
        }
    }

    private void PopulateFieldsFromConfig()
    {
        rommHostInput.Text = appInstance.configManager.RomMHost;
        rommUserInput.Text = appInstance.configManager.RomMUsername;
        rommPasswordInput.Text = appInstance.configManager.RomMPassword;
        rommApiKeyInput.Text = appInstance.configManager.RomMApiKey;
    }

    private async void AttemptAutoLogin()
    {
        if (!string.IsNullOrEmpty(appInstance.configManager.RomMHost) ||
            !string.IsNullOrEmpty(appInstance.configManager.RomMUsername) ||
            !string.IsNullOrEmpty(appInstance.configManager.RomMPassword) ||
            !string.IsNullOrEmpty(appInstance.configManager.RomMApiKey))
        {
            await Task.Delay(100);
            OnLoginButtonPressed();
        }
    }

    private async void OnLoginButtonPressed()
    {
        if (errorLabel != null)
        {
            errorLabel.Text = "Authenticating...";
        }

        loginButton.Disabled = true;

        (bool isSuccess, string errorMessage) result = (false, "An unknown error occurred.");
        
        string host = rommHostInput.Text;
        string username = rommUserInput.Text;
        string password = rommPasswordInput.Text;
        string apiKey = rommApiKeyInput.Text;
        StartupTimeline.Mark("auth start");
        result = await appInstance.rommApi.AuthenticateAsync(username, password, host, apiKey);
        StartupTimeline.Mark("auth done");
        GD.Print($"[Login] sign-in {(result.isSuccess ? "succeeded" : "failed")}.");

        if (result.isSuccess)
        {
            appInstance.configManager.SaveRomMCredentials(host, username, password, apiKey);
            appInstance.configManager.SaveValidLoginLastUsed(true);
        }
        

        if (result.isSuccess)
        {
            if (errorLabel != null)
            {
                errorLabel.Text = "Success!";
            }

            GetTree().ChangeSceneToFile("res://scenes/login/loading_screen.tscn");
        }
        
        else
        {
            if (errorLabel != null)
            {
                errorLabel.Text = result.errorMessage;
            }
        }

        loginButton.Disabled = false;
    }
}
