using System.Collections.Generic;
using System.Linq;

public class ControllerSetupMethod
{
    public string ConsentValue { get; }
    public string Title { get; }
    public string Description { get; }

    private ControllerSetupMethod(string consentValue, string title, string description)
    {
        ConsentValue = consentValue;
        Title = title;
        Description = description;
    }

    public static readonly ControllerSetupMethod SteamInput = new ControllerSetupMethod(
        ConfigManager.ControllerMappingConsentSteamInput,
        "Steam Input",
        "Use if you launched the app through Steam with Steam Input turned on. Steam handles your controllers, and RomM points each emulator at the right one.");

    public static readonly ControllerSetupMethod VirtualPads = new ControllerSetupMethod(
        ConfigManager.ControllerMappingConsentAccepted,
        "ViGEm",
        "Use if you launch the app on its own. Xbox controllers go straight to the emulator, and other controllers (PlayStation, Switch Pro, 8BitDo) appear as an Xbox 360 pad through a small driver RomM installs.");

    public static readonly ControllerSetupMethod Manual = new ControllerSetupMethod(
        ConfigManager.ControllerMappingConsentDeclined,
        "Manual",
        "Set up controllers in each emulator yourself. RomM leaves emulator controller settings alone.");

    public static IReadOnlyList<ControllerSetupMethod> All { get; } = new[] { SteamInput, VirtualPads, Manual };

    public static bool IsLaunchedThroughSteam => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId"));

    public static ControllerSetupMethod Recommended => IsLaunchedThroughSteam ? SteamInput : VirtualPads;

    public static ControllerSetupMethod FromConsent(string consentValue)
    {
        return All.FirstOrDefault(method => method.ConsentValue == consentValue);
    }
}
