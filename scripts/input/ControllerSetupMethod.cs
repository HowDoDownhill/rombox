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
        "Start the app from Steam with Steam Input turned on. Steam handles every controller, and RomM points each emulator at the right one for every player.");

    public static readonly ControllerSetupMethod Manual = new ControllerSetupMethod(
        ConfigManager.ControllerMappingConsentDeclined,
        "Manual",
        "Set up controllers in each emulator yourself. RomM leaves emulator controller settings alone.");

    public static IReadOnlyList<ControllerSetupMethod> All { get; } = new[] { SteamInput, Manual };

    public static bool IsLaunchedThroughSteam => !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SteamAppId"));

    public static ControllerSetupMethod Recommended => SteamInput;

    public static int IndexOf(ControllerSetupMethod method)
    {
        return All.ToList().IndexOf(method);
    }

    public static ControllerSetupMethod FromConsent(string consentValue)
    {
        return All.FirstOrDefault(method => method.ConsentValue == consentValue);
    }
}
