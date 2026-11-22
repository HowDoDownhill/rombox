using Godot;

// A small filled label on a game's card or row, such as RECOMP.
public partial class GameBadge : PanelContainer
{
    private const int FontSize = 13;
    private const int CornerRadius = 6;

    private readonly Label label = new Label();

    public GameBadge()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SizeFlagsVertical = SizeFlags.ShrinkCenter;
        Visible = false;

        Color fill = Appearance.Accent;
        var style = new StyleBoxFlat { BgColor = fill };
        style.SetCornerRadiusAll(CornerRadius);
        style.ContentMarginLeft = 8;
        style.ContentMarginRight = 8;
        style.ContentMarginTop = 2;
        style.ContentMarginBottom = 2;
        AddThemeStyleboxOverride("panel", style);

        label.MouseFilter = MouseFilterEnum.Ignore;
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", fill.Luminance > 0.5f ? new Color(0.05f, 0.06f, 0.1f) : Colors.White);
        AddChild(label);
    }

    public string Text
    {
        set
        {
            label.Text = (value ?? "").ToUpperInvariant();
            Visible = !string.IsNullOrEmpty(value);
        }
    }
}
