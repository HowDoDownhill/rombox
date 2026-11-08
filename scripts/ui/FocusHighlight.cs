using Godot;

public static class FocusHighlight
{
    private const int CornerRadius = 10;
    private const int SimpleEdgeWidth = 3;
    private const int GameCardCornerRadius = 8;
    private const int RingWidth = 4;

    // How far the Simple ring sits outside a game card. The carousel's card ring uses the same gap.
    public const int RingGap = 6;

    // One style for every selection indicator, recoloured in place when the appearance changes.
    public static readonly StyleBoxFlat IndicatorStyle = CreateIndicatorStyle();

    // Simple marks the selected game in the grid and list with an accent ring and no fill.
    public static readonly StyleBoxFlat RingStyle = CreateRingStyle();

    // Bumped by Restyle, so indicators know to redraw with the new colours.
    public static int StyleVersion { get; private set; }

    public static StyleBoxFlat CreateStyle(float horizontalPadding = -1, float verticalPadding = -1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = new Color(1, 1, 1, 0),
            BorderColor = new Color(1, 1, 1, 0),
            DrawCenter = false,
            AntiAliasing = true
        };

        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(CornerRadius);

        if (horizontalPadding >= 0)
        {
            style.ContentMarginLeft = horizontalPadding;
            style.ContentMarginRight = horizontalPadding;
        }

        if (verticalPadding >= 0)
        {
            style.ContentMarginTop = verticalPadding;
            style.ContentMarginBottom = verticalPadding;
        }

        return style;
    }

    private static StyleBoxFlat CreateIndicatorStyle()
    {
        var style = new StyleBoxFlat
        {
            DrawCenter = true,
            AntiAliasing = true
        };

        style.SetCornerRadiusAll(CornerRadius);
        ApplyIndicatorColors(style);
        return style;
    }

    private static StyleBoxFlat CreateRingStyle()
    {
        var style = new StyleBoxFlat
        {
            DrawCenter = false,
            AntiAliasing = true
        };

        style.SetBorderWidthAll(RingWidth);
        style.SetCornerRadiusAll(GameCardCornerRadius + RingGap);
        style.BorderColor = Appearance.Accent;
        return style;
    }

    // Glass marks the selection with a faint wash and hairline. Simple outlines it in the accent colour.
    public static void Restyle()
    {
        ApplyIndicatorColors(IndicatorStyle);
        RingStyle.BorderColor = Appearance.Accent;
        StyleVersion++;
    }

    private static void ApplyIndicatorColors(StyleBoxFlat style)
    {
        if (Appearance.IsSimple)
        {
            style.BgColor = Appearance.Overlay(0.06f, 0.05f);
            style.BorderColor = Appearance.Accent;
            style.SetBorderWidthAll(SimpleEdgeWidth);
        }
        else
        {
            style.BgColor = Appearance.Overlay(0.12f, 0.07f);
            style.BorderColor = Appearance.Overlay(0.16f, 0.16f);
            style.SetBorderWidthAll(1);
        }
    }

    public static void Set(Control item, Control area, bool focused)
    {
        SelectionHighlighter.Set(item, area, focused);
    }
}
