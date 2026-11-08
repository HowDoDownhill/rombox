using Godot;
using System;

// The app's look. Glass draws an animated background and frosted panels. Simple draws flat colours
// and opaque panels and turns on Godot's low-processor mode, so the screen is redrawn only when
// something on it changes. Either style takes dark text when the palette's background is light.
public static class Appearance
{
    public const string GlassStyle = "Glass";
    public const string SimpleStyle = "Simple";
    public static readonly string[] Styles = { GlassStyle, SimpleStyle };

    private const string BackgroundMaterialPath = "res://assets/materials/moving_background.tres";
    private const string PanelMaterialPath = "res://assets/materials/mica_panel.tres";
    private const string FlatBackgroundShaderPath = "res://assets/shaders/backgrounds/bg_flat.gdshader";
    private const string MicaShaderPath = "res://assets/shaders/mica_panel.gdshader";
    private const string FlatPanelShaderPath = "res://assets/shaders/flat_panel.gdshader";
    private const string LogoMaterialPath = "res://assets/materials/logo_adapt.tres";

    // For platform logos and icons drawn straight on the background. Many are white, so on a light
    // palette their grey parts are inked in the text colour.
    public static ShaderMaterial LogoMaterial => GD.Load<ShaderMaterial>(LogoMaterialPath);

    public const float DefaultLuminosityFloor = 0.14f;

    // Perceived luminance above which a palette's background counts as light.
    private const float LightBackgroundLuminance = 0.5f;

    private const float DarkVignette = 0.7f;
    private const float LightVignette = 0.2f;

    // Simple panels sit one step off the background: lighter on dark palettes, near white on light ones.
    private const float DarkPanelLift = 0.08f;
    private const float LightPanelLift = 0.7f;

    // Below this saturation a palette has no hue worth keeping, and the accent goes neutral.
    private const float NeutralAccentSaturation = 0.05f;

    public static bool IsSimple { get; private set; }
    public static bool IsLight { get; private set; }
    public static Color Accent { get; private set; } = new Color(0.78f, 0.84f, 0.97f);

    // Raised after every Apply, so code that drew with the old colours can redraw.
    public static event Action Changed;

    public static Color Text => IsLight ? new Color(0.11f, 0.12f, 0.14f) : new Color(0.929f, 0.937f, 0.961f);
    public static Color ButtonText => IsLight ? new Color(0.17f, 0.18f, 0.21f) : new Color(0.882f, 0.894f, 0.925f);
    public static Color StrongText => IsLight ? new Color(0.05f, 0.06f, 0.07f) : new Color(0.961f, 0.965f, 0.98f);
    public static Color BodyText => IsLight ? new Color(0.18f, 0.19f, 0.22f) : new Color(0.851f, 0.863f, 0.898f);
    public static Color CaptionText => IsLight ? new Color(0.36f, 0.38f, 0.44f) : new Color(0.635f, 0.659f, 0.733f);
    public static Color MutedText => IsLight ? new Color(0.4f, 0.4f, 0.42f) : new Color(0.6f, 0.6f, 0.6f);
    public static Color WarningText => IsLight ? new Color(0.62f, 0.34f, 0.0f) : new Color(1.0f, 0.75f, 0.38f);
    public static Color AccentText => IsLight ? new Color(0.1f, 0.36f, 0.7f) : new Color(0.6f, 0.8f, 1.0f);

    // Icons drawn white in the artwork are multiplied by this, so they follow the text.
    public static Color IconTint => IsLight ? new Color(0.2f, 0.21f, 0.24f) : Colors.White;

    // White over dark palettes and black over light ones, for washes such as a pressed row.
    public static Color Overlay(float darkAlpha, float lightAlpha)
    {
        return IsLight ? new Color(0, 0, 0, lightAlpha) : new Color(1, 1, 1, darkAlpha);
    }

    public static bool IsLightBackground(Color background)
    {
        return background.R * 0.2126f + background.G * 0.7152f + background.B * 0.0722f > LightBackgroundLuminance;
    }

    // Resolves the saved palette without a selected system, for screens shown before the library.
    public static void ApplySaved(ConfigManager configManager)
    {
        string themeName = ConfigManager.IsSystemTheme(configManager.AppTheme) ? ConfigManager.DefaultThemeName : configManager.AppTheme;

        if (ConfigManager.Themes.TryGetValue(themeName, out var colors) || ConfigManager.Themes.TryGetValue(ConfigManager.DefaultThemeName, out colors))
        {
            Apply(configManager.AppStyle, configManager.AppBackground, colors, DefaultLuminosityFloor);
        }
    }

    public static void Apply(string style, string backgroundName, (Color Bg, Color Primary, Color Secondary, Color Panel) colors, float luminosityFloor)
    {
        IsSimple = style == SimpleStyle;
        IsLight = IsLightBackground(colors.Bg);
        Accent = DeriveAccent(colors.Primary, colors.Secondary);

        ApplyBackground(backgroundName, colors);
        ApplyPanels(colors, luminosityFloor);

        Theme theme = ThemeDB.GetProjectTheme();
        if (theme != null)
        {
            ApplyTextColors(theme);
            ApplyThemeStyles(theme);
            ApplySwitchIcons(theme);
        }

        LogoMaterial?.SetShaderParameter("strength", IsLight ? 1.0f : 0.0f);
        LogoMaterial?.SetShaderParameter("ink_color", Text);

        FocusHighlight.Restyle();
        OS.LowProcessorUsageMode = IsSimple;

        Changed?.Invoke();
    }

    private static void ApplyBackground(string backgroundName, (Color Bg, Color Primary, Color Secondary, Color Panel) colors)
    {
        var material = GD.Load<ShaderMaterial>(BackgroundMaterialPath);
        if (material == null)
        {
            return;
        }

        // Swap the shader before setting parameters, or the values land on a shader that isn't going to render them.
        var shader = GD.Load<Shader>(IsSimple ? FlatBackgroundShaderPath : ConfigManager.BackgroundShaderPath(backgroundName));
        if (shader != null && material.Shader != shader)
        {
            material.Shader = shader;
        }

        material.SetShaderParameter("bg_color", colors.Bg);
        material.SetShaderParameter("primary_color", colors.Primary);
        material.SetShaderParameter("secondary_color", colors.Secondary);
        material.SetShaderParameter("vignette_amount", IsLight ? LightVignette : DarkVignette);
    }

    private static void ApplyPanels((Color Bg, Color Primary, Color Secondary, Color Panel) colors, float luminosityFloor)
    {
        var material = GD.Load<ShaderMaterial>(PanelMaterialPath);
        if (material == null)
        {
            return;
        }

        var shader = GD.Load<Shader>(IsSimple ? FlatPanelShaderPath : MicaShaderPath);
        if (shader != null && material.Shader != shader)
        {
            material.Shader = shader;
        }

        Color tint;

        if (IsSimple)
        {
            tint = colors.Bg.Lerp(Colors.White, IsLight ? LightPanelLift : DarkPanelLift);
            tint.A = 1.0f;
        }
        else
        {
            // Frost over a dark palette tints toward its darkest colour; over a light one, toward its background.
            tint = IsLight ? colors.Bg : Darkest(colors.Bg, colors.Primary, colors.Secondary);
            tint.A = colors.Panel.A;
        }

        material.SetShaderParameter("mix_color", tint);
        material.SetShaderParameter("luminosity_floor", IsLight ? 0.0f : luminosityFloor);
    }

    private static Color DeriveAccent(Color primary, Color secondary)
    {
        Color source = primary.S * primary.V >= secondary.S * secondary.V ? primary : secondary;

        if (source.S < NeutralAccentSaturation)
        {
            return IsLight ? new Color(0.3f, 0.31f, 0.34f) : new Color(0.85f, 0.86f, 0.9f);
        }

        return IsLight ? Color.FromHsv(source.H, 0.75f, 0.78f) : Color.FromHsv(source.H, 0.5f, 1.0f);
    }

    private static void ApplyTextColors(Theme theme)
    {
        SetColor(theme, "font_color", "Label", Text);
        SetColor(theme, "font_color", "LineEdit", Text);
        SetColor(theme, "font_placeholder_color", "LineEdit", IsLight ? new Color(Text, 0.45f) : new Color(0.875f, 0.875f, 0.875f, 0.6f));
        SetColor(theme, "caret_color", "LineEdit", IsLight ? Text : new Color(0.95f, 0.95f, 0.95f));

        SetColor(theme, "font_color", "Button", ButtonText);
        SetColor(theme, "font_disabled_color", "Button", new Color(Text, 0.38f));
        foreach (string state in new[] { "font_focus_color", "font_hover_color", "font_hover_pressed_color", "font_pressed_color" })
        {
            SetColor(theme, state, "Button", StrongText);
        }

        SetColor(theme, "font_color", "CaptionLabel", CaptionText);
        SetColor(theme, "default_color", "RichTextLabel", BodyText);
        SetColor(theme, "font_color", "MutedLabel", MutedText);
        SetColor(theme, "font_color", "WarningLabel", WarningText);
        SetColor(theme, "font_color", "AccentLabel", AccentText);

        foreach (string state in new[] { "font_color", "font_focus_color", "font_hover_color", "font_pressed_color" })
        {
            SetColor(theme, state, "WarningListButton", WarningText);
        }
    }

    private const int TextFieldFocusWidth = 2;
    private const int TextFieldFocusRadius = 3;

    private static StyleBoxFlat textFieldFocusStyle;
    private static StyleBoxFlat textFieldNormalStyle;
    private static Color textFieldDefaultBackground;

    // Godot's own outline sits outside the field, so a scrolling list cuts off the edges touching it.
    private static void ApplyTextFieldFocus(Theme theme)
    {
        if (textFieldFocusStyle == null)
        {
            textFieldFocusStyle = new StyleBoxFlat { DrawCenter = false, AntiAliasing = true };
            textFieldFocusStyle.SetBorderWidthAll(TextFieldFocusWidth);
            textFieldFocusStyle.SetCornerRadiusAll(TextFieldFocusRadius);
            theme.SetStylebox("focus", "LineEdit", textFieldFocusStyle);

            textFieldNormalStyle = ThemeDB.GetDefaultTheme().GetStylebox("normal", "LineEdit").Duplicate() as StyleBoxFlat;
            textFieldDefaultBackground = textFieldNormalStyle.BgColor;
            theme.SetStylebox("normal", "LineEdit", textFieldNormalStyle);
        }

        textFieldFocusStyle.BorderColor = Accent;
        textFieldNormalStyle.BgColor = IsLight ? Overlay(0.0f, 0.06f) : textFieldDefaultBackground;
    }

    private static void ApplyThemeStyles(Theme theme)
    {
        ApplyTextFieldFocus(theme);

        if (theme.GetStylebox("pressed", "ListButton") is StyleBoxFlat pressed)
        {
            pressed.BgColor = Overlay(0.18f, 0.1f);
        }

        if (theme.GetStylebox("background", "ProgressBar") is StyleBoxFlat track)
        {
            track.BgColor = Overlay(0.1f, 0.1f);
        }

        if (theme.GetStylebox("fill", "ProgressBar") is StyleBoxFlat fill)
        {
            fill.BgColor = Accent;
            fill.ShadowColor = new Color(Accent, IsLight ? 0.2f : 0.35f);
        }
    }

    private const int SwitchWidth = 46;
    private const int SwitchHeight = 26;
    private static string appliedSwitchKey;

    // Godot's own switch is drawn in light greys that vanish on a light palette. Light palettes and
    // the Simple style get a switch drawn here: accent track when on, a neutral track when off.
    // Dark Glass keeps Godot's switch.
    private static void ApplySwitchIcons(Theme theme)
    {
        string[] names = { "checked", "unchecked", "checked_disabled", "unchecked_disabled", "checked_mirrored", "unchecked_mirrored", "checked_disabled_mirrored", "unchecked_disabled_mirrored" };
        bool custom = IsLight || IsSimple;
        string key = custom ? $"{IsLight}:{Accent.ToHtml()}" : "";

        if (key == appliedSwitchKey)
        {
            return;
        }

        appliedSwitchKey = key;

        if (!custom)
        {
            foreach (string name in names)
            {
                if (theme.HasIcon(name, "CheckButton"))
                {
                    theme.ClearIcon(name, "CheckButton");
                }
            }
            return;
        }

        Color offTrack = IsLight ? new Color(0.72f, 0.73f, 0.77f) : new Color(0.32f, 0.33f, 0.37f);
        Color knob = Colors.White;

        foreach (string name in names)
        {
            bool on = name.StartsWith("checked");
            bool disabled = name.Contains("disabled");
            bool mirrored = name.EndsWith("mirrored");
            bool knobRight = on != mirrored;
            theme.SetIcon(name, "CheckButton", DrawSwitch(on ? Accent : offTrack, knob, knobRight, disabled ? 0.4f : 1.0f));
        }
    }

    private static ImageTexture DrawSwitch(Color track, Color knob, bool knobRight, float opacity)
    {
        float radius = SwitchHeight / 2.0f;
        float knobRadius = radius - 3.0f;
        float knobX = knobRight ? SwitchWidth - radius : radius;
        // Invariant formatting: a comma decimal separator would break the SVG.
        string rect = FormattableString.Invariant($"<rect width='{SwitchWidth}' height='{SwitchHeight}' rx='{radius}' fill='#{track.ToHtml(false)}' fill-opacity='{opacity:0.##}'/>");
        string circle = FormattableString.Invariant($"<circle cx='{knobX}' cy='{radius}' r='{knobRadius}' fill='#{knob.ToHtml(false)}' fill-opacity='{opacity:0.##}'/>");
        string svg = $"<svg xmlns='http://www.w3.org/2000/svg' width='{SwitchWidth}' height='{SwitchHeight}'>{rect}{circle}</svg>";

        var image = new Image();
        image.LoadSvgFromString(svg);
        return ImageTexture.CreateFromImage(image);
    }

    // Theme.SetColor always announces a change, and every control in the tree then re-reads its theme.
    // Skipping unchanged values keeps a system switch under Match System from relaying out the whole UI.
    private static void SetColor(Theme theme, string name, string themeType, Color color)
    {
        if (!theme.HasColor(name, themeType) || theme.GetColor(name, themeType) != color)
        {
            theme.SetColor(name, themeType, color);
        }
    }

    private static Color Darkest(params Color[] candidates)
    {
        Color darkest = candidates[0];
        float min = float.MaxValue;

        foreach (Color candidate in candidates)
        {
            float luminance = candidate.R * 0.2126f + candidate.G * 0.7152f + candidate.B * 0.0722f;
            if (luminance < min)
            {
                min = luminance;
                darkest = candidate;
            }
        }

        return darkest;
    }
}
