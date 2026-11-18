using Godot;

// Applies the palette saved in settings without a selected system, for screens shown before the library.
public static class SavedAppearance
{
    public static void Apply(ConfigManager configManager)
    {
        string themeName = ConfigManager.IsSystemTheme(configManager.AppTheme) ? ConfigManager.DefaultThemeName : configManager.AppTheme;

        if (ConfigManager.Themes.TryGetValue(themeName, out var colors) || ConfigManager.Themes.TryGetValue(ConfigManager.DefaultThemeName, out colors))
        {
            Appearance.Apply(configManager.AppStyle, configManager.AppBackground, colors, Appearance.DefaultLuminosityFloor);
        }
    }
}
