using Godot;
using System;
using System.Collections.Generic;

public static class ControllerGlyph
{
    private const int GlyphSize = 48;
    private const int GlyphSpacing = 10;
    private const int CombinedIconGap = 12;
    private const string IconAssetRoot = "res://addons/controller_icons/assets/";

    private static readonly Dictionary<int, string> NeutralIconPaths = new Dictionary<int, string>
    {
        { (int)JoyButton.A, "positional/south" },
        { (int)JoyButton.B, "positional/east" },
        { (int)JoyButton.X, "positional/west" },
        { (int)JoyButton.Y, "positional/north" },
        { (int)JoyButton.DpadUp, "xbox360/dpad_up" },
        { (int)JoyButton.DpadDown, "xbox360/dpad_down" },
        { (int)JoyButton.DpadLeft, "xbox360/dpad_left" },
        { (int)JoyButton.DpadRight, "xbox360/dpad_right" }
    };

    private static readonly Dictionary<int, string[]> NeutralCombinedIconPaths = new Dictionary<int, string[]>
    {
        { (int)JoyButton.Start, new[] { "xboxone/menu", "ps5/options", "switch/plus" } },
        { (int)JoyButton.Back, new[] { "xboxone/view", "ps5/share", "switch/minus" } }
    };

    private class GlyphButton
    {
        public WeakReference<Button> Button;
        public string ActionName;
        public ControllerIconTexture Glyph;
    }

    private static readonly List<GlyphButton> glyphButtons = new List<GlyphButton>();
    private static readonly Dictionary<int, Texture2D> combinedIcons = new Dictionary<int, Texture2D>();
    private static bool showsNeutralPrompts;
    private static bool isFollowingLastController;

    public static ControllerIconTexture For(string actionName)
    {
        var glyph = new ControllerIconTexture();
        glyph.path = actionName;
        glyph.show_mode = ControllerIcons.EShowMode.CONTROLLER;
        return glyph;
    }

    public static void Apply(Button button, string actionName)
    {
        FollowLastController();

        var glyphButton = new GlyphButton
        {
            Button = new WeakReference<Button>(button),
            ActionName = actionName,
            Glyph = For(actionName)
        };

        button.ExpandIcon = false;
        button.IconAlignment = HorizontalAlignment.Left;
        button.AddThemeConstantOverride("h_separation", GlyphSpacing);
        glyphButtons.Add(glyphButton);
        ShowPrompt(glyphButton);
    }

    private static void FollowLastController()
    {
        if (isFollowingLastController || ControllerIcons.CI == null)
        {
            return;
        }

        isFollowingLastController = true;
        showsNeutralPrompts = ControllerIcons.CI.LastInputType == ControllerIcons.EInputType.CONTROLLER;
        ControllerIcons.CI.InputTypeChanged += (inputType, controller) => ShowNeutralPrompts(inputType == (int)ControllerIcons.EInputType.CONTROLLER);
    }

    private static void ShowNeutralPrompts(bool neutral)
    {
        if (neutral == showsNeutralPrompts)
        {
            return;
        }

        showsNeutralPrompts = neutral;
        glyphButtons.RemoveAll(entry => !entry.Button.TryGetTarget(out Button button) || !GodotObject.IsInstanceValid(button));

        foreach (GlyphButton glyphButton in glyphButtons)
        {
            ShowPrompt(glyphButton);
        }
    }

    private static void ShowPrompt(GlyphButton glyphButton)
    {
        if (!glyphButton.Button.TryGetTarget(out Button button) || !GodotObject.IsInstanceValid(button))
        {
            return;
        }

        int buttonIndex = showsNeutralPrompts ? ReadJoypadButton(glyphButton.ActionName) : -1;
        Texture2D combinedIcon = NeutralCombinedIconPaths.TryGetValue(buttonIndex, out string[] combinedPaths)
            ? LoadCombinedIcon(buttonIndex, combinedPaths)
            : null;

        if (combinedIcon != null)
        {
            button.Icon = combinedIcon;
            button.AddThemeConstantOverride("icon_max_width", combinedIcon.GetWidth() * GlyphSize / combinedIcon.GetHeight());
            return;
        }

        glyphButton.Glyph.path = NeutralIconPaths.TryGetValue(buttonIndex, out string neutralPath) ? neutralPath : glyphButton.ActionName;
        glyphButton.Glyph.force_controller_icon_style = showsNeutralPrompts ? ControllerSettings.Devices.XBOX360 : ControllerSettings.Devices.NONE;
        button.Icon = glyphButton.Glyph;
        button.AddThemeConstantOverride("icon_max_width", GlyphSize);
    }

    private static int ReadJoypadButton(string actionName)
    {
        if (!InputMap.HasAction(actionName))
        {
            return -1;
        }

        foreach (InputEvent actionEvent in InputMap.ActionGetEvents(actionName))
        {
            if (actionEvent is InputEventJoypadButton joypadButton)
            {
                return (int)joypadButton.ButtonIndex;
            }
        }

        return -1;
    }

    private static Texture2D LoadCombinedIcon(int buttonIndex, string[] iconPaths)
    {
        if (combinedIcons.TryGetValue(buttonIndex, out Texture2D cachedIcon))
        {
            return cachedIcon;
        }

        var images = new List<Image>();

        foreach (string iconPath in iconPaths)
        {
            Image image = GD.Load<Texture2D>(IconAssetRoot + iconPath + ".png")?.GetImage();

            if (image == null)
            {
                continue;
            }

            if (image.IsCompressed())
            {
                image.Decompress();
            }

            image.Convert(Image.Format.Rgba8);
            images.Add(image);
        }

        if (images.Count == 0)
        {
            return null;
        }

        int iconHeight = images[0].GetHeight();
        int combinedWidth = CombinedIconGap * (images.Count - 1);

        foreach (Image image in images)
        {
            image.Resize(image.GetWidth() * iconHeight / image.GetHeight(), iconHeight);
            combinedWidth += image.GetWidth();
        }

        Image combinedImage = Image.CreateEmpty(combinedWidth, iconHeight, false, Image.Format.Rgba8);
        int offset = 0;

        foreach (Image image in images)
        {
            combinedImage.BlitRect(image, new Rect2I(0, 0, image.GetWidth(), image.GetHeight()), new Vector2I(offset, 0));
            offset += image.GetWidth() + CombinedIconGap;
        }

        Texture2D combinedIcon = ImageTexture.CreateFromImage(combinedImage);
        combinedIcons[buttonIndex] = combinedIcon;
        return combinedIcon;
    }
}
