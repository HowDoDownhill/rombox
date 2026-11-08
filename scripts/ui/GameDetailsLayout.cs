using Godot;
using System.Collections.Generic;

// Lays out the game details panel. Both layouts are built once. The logo, title, description and
// screenshot strip move into whichever one is showing.
public sealed class GameDetailsLayout
{
    public const string Hero = "Hero";
    public const string Box = "Box";

    public static readonly (string Id, string Label)[] Options = { (Hero, "Large Image"), (Box, "Box Art") };

    public const int MaximumScreenshots = 5;

    private const int FactFontSize = 21;
    private const int DescriptionFontSize = 22;
    private const int LogoHeight = 130;
    private const float BoxColumnWidth = 300.0f;
    private const float BoxCoverHeight = 400.0f;
    private const float BoxScreenshotHeight = 300.0f;
    private const float HeroAreaStretch = 1.2f;
    private const float PlaceholderAlpha = 0.18f;
    private const float ScrimAlpha = 0.82f;
    private const float ScrimHeightShare = 0.6f;
    private const int ThumbnailRingWidth = 3;

    private static readonly Vector2 HeroThumbnailSize = new Vector2(160, 90);
    private static readonly Vector2 BoxThumbnailSize = new Vector2(100, 56);

    private readonly Control heroTree;
    private readonly Control boxTree;
    private readonly VBoxContainer heroLogoSlot;
    private readonly VBoxContainer heroDescriptionSlot;
    private readonly VBoxContainer heroStripSlot;
    private readonly VBoxContainer boxLogoSlot;
    private readonly VBoxContainer boxDescriptionSlot;
    private readonly VBoxContainer boxStripSlot;
    private readonly TextureRect heroImage;
    private readonly TextureRect boxScreenshot;
    private readonly Panel heroBackdrop;
    private readonly Panel coverPlaceholder;
    private readonly VBoxContainer boxFacts;
    private readonly Label metaStatus;
    private readonly Label metaDetails;
    private readonly TextureRect logo;
    private readonly Label title;
    private readonly RichTextLabel description;
    private readonly GridContainer strip;
    private readonly TextureRect cover;
    private readonly List<(TextureRect Thumbnail, Panel Ring)> thumbnails = new List<(TextureRect, Panel)>();
    private readonly StyleBoxFlat ringStyle = new StyleBoxFlat { DrawCenter = false, AntiAliasing = true };

    private string activeLayout;

    public GameDetailsLayout(MainScene mainScene, string layout)
    {
        Control panel = mainScene.detailsPanelContainer;
        logo = mainScene.gameMarquee;
        description = mainScene.gameDescription;
        strip = mainScene.gameScreenshotsFlow;

        Node oldLogoParent = logo.GetParent();
        Node oldDescriptionParent = description.GetParent().GetParent();
        oldLogoParent.RemoveChild(logo);
        description.GetParent().RemoveChild(description);
        strip.GetParent().RemoveChild(strip);
        oldLogoParent.QueueFree();
        oldDescriptionParent.QueueFree();

        title = new Label
        {
            ThemeTypeVariation = "DisplayLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MaxLinesVisible = 2
        };
        cover = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        };

        logo.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        logo.StretchMode = TextureRect.StretchModeEnum.KeepAspect;
        logo.CustomMinimumSize = new Vector2(0, LogoHeight);
        description.AddThemeFontSizeOverride("normal_font_size", DescriptionFontSize);
        description.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        strip.Columns = MaximumScreenshots;
        strip.AddThemeConstantOverride("h_separation", 10);

        mainScene.gameTitle = title;
        mainScene.gameCover = cover;
        mainScene.gameScreenshotsScroll = null;

        metaStatus = new Label { ThemeTypeVariation = "AccentLabel" };
        metaStatus.AddThemeFontSizeOverride("font_size", FactFontSize);
        metaDetails = new Label
        {
            ThemeTypeVariation = "DetailValueLabel",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };

        heroImage = NewImage(TextureRect.StretchModeEnum.KeepAspectCovered);
        heroBackdrop = NewPlaceholder();
        heroTree = BuildHeroTree(out heroLogoSlot, out heroDescriptionSlot, out heroStripSlot);

        coverPlaceholder = NewPlaceholder();
        boxScreenshot = NewImage(TextureRect.StretchModeEnum.KeepAspectCentered);
        boxScreenshot.CustomMinimumSize = new Vector2(0, BoxScreenshotHeight);
        boxFacts = new VBoxContainer();
        boxFacts.AddThemeConstantOverride("separation", 10);
        boxTree = BuildBoxTree(out boxLogoSlot, out boxDescriptionSlot, out boxStripSlot);

        panel.AddChild(heroTree);
        panel.AddChild(boxTree);
        panel.MoveChild(heroTree, 0);
        panel.MoveChild(boxTree, 1);

        Apply(layout);
    }

    public void Apply(string layout)
    {
        string next = layout == Box ? Box : Hero;

        if (next == activeLayout)
        {
            return;
        }

        activeLayout = next;
        bool isHero = next == Hero;
        heroTree.Visible = isHero;
        boxTree.Visible = !isHero;

        Move(logo, isHero ? heroLogoSlot : boxLogoSlot);
        Move(title, isHero ? heroLogoSlot : boxLogoSlot);
        Move(description, isHero ? heroDescriptionSlot : boxDescriptionSlot);
        Move(strip, isHero ? heroStripSlot : boxStripSlot);

        // Over the hero image the title always sits on a dark fade, on Light themes too.
        if (isHero)
        {
            title.AddThemeColorOverride("font_color", Colors.White);
        }
        else
        {
            title.RemoveThemeColorOverride("font_color");
        }

        foreach (var (thumbnail, _) in thumbnails)
        {
            thumbnail.CustomMinimumSize = ThumbnailSize;
        }
    }

    private Vector2 ThumbnailSize => activeLayout == Box ? BoxThumbnailSize : HeroThumbnailSize;

    private static void Move(Control widget, Node parent)
    {
        widget.GetParent()?.RemoveChild(widget);
        parent.AddChild(widget);
    }

    private static TextureRect NewImage(TextureRect.StretchModeEnum stretch)
    {
        return new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = stretch };
    }

    private static Panel NewPlaceholder()
    {
        var placeholder = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        var style = new StyleBoxFlat { BgColor = new Color(0, 0, 0, PlaceholderAlpha) };
        style.SetCornerRadiusAll(8);
        placeholder.AddThemeStyleboxOverride("panel", style);
        return placeholder;
    }

    private static void Fill(Control child, Control parent)
    {
        parent.AddChild(child);
        child.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    }

    private static MarginContainer NewMargin(int left, int top, int right, int bottom)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", left);
        margin.AddThemeConstantOverride("margin_top", top);
        margin.AddThemeConstantOverride("margin_right", right);
        margin.AddThemeConstantOverride("margin_bottom", bottom);
        return margin;
    }

    private static TextureRect NewScrim()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0, 0, 0, 0));
        gradient.SetColor(1, new Color(0, 0, 0, ScrimAlpha));

        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(0, 1),
            Width = 4,
            Height = 128
        };

        return new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
    }

    private Control BuildHeroTree(out VBoxContainer logoSlot, out VBoxContainer descriptionSlot, out VBoxContainer stripSlot)
    {
        var tree = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };

        MarginContainer imageMargin = NewMargin(8, 8, 8, 0);
        imageMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        imageMargin.SizeFlagsStretchRatio = HeroAreaStretch;
        tree.AddChild(imageMargin);

        var stack = new Control { ClipContents = true };
        imageMargin.AddChild(stack);
        Fill(heroBackdrop, stack);
        Fill(heroImage, stack);

        TextureRect scrim = NewScrim();
        stack.AddChild(scrim);
        scrim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scrim.AnchorTop = 1.0f - ScrimHeightShare;

        MarginContainer logoMargin = NewMargin(32, 24, 32, 20);
        Fill(logoMargin, stack);
        logoSlot = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        logoMargin.AddChild(logoSlot);

        MarginContainer contentMargin = NewMargin(40, 18, 40, 24);
        contentMargin.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        tree.AddChild(contentMargin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 14);
        contentMargin.AddChild(content);

        var meta = new HBoxContainer();
        meta.AddThemeConstantOverride("separation", 14);
        meta.AddChild(metaStatus);
        meta.AddChild(metaDetails);
        content.AddChild(meta);

        descriptionSlot = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        content.AddChild(descriptionSlot);

        stripSlot = new VBoxContainer();
        content.AddChild(stripSlot);
        return tree;
    }

    private Control BuildBoxTree(out VBoxContainer logoSlot, out VBoxContainer descriptionSlot, out VBoxContainer stripSlot)
    {
        MarginContainer tree = NewMargin(28, 28, 28, 24);
        tree.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 28);
        tree.AddChild(columns);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(BoxColumnWidth, 0) };
        left.AddThemeConstantOverride("separation", 20);
        columns.AddChild(left);

        var coverFrame = new Control { CustomMinimumSize = new Vector2(BoxColumnWidth, BoxCoverHeight) };
        left.AddChild(coverFrame);
        Fill(coverPlaceholder, coverFrame);
        Fill(cover, coverFrame);
        left.AddChild(boxFacts);

        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeConstantOverride("separation", 14);
        columns.AddChild(right);

        logoSlot = new VBoxContainer();
        right.AddChild(logoSlot);

        descriptionSlot = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        right.AddChild(descriptionSlot);

        right.AddChild(boxScreenshot);

        stripSlot = new VBoxContainer();
        right.AddChild(stripSlot);
        return tree;
    }

    // The large image. A screenshot fills both layouts; the cover only stands in on the hero.
    public void SetFeaturedImage(Texture2D texture, bool isScreenshot)
    {
        heroImage.Texture = texture;
        heroBackdrop.Visible = texture == null;
        boxScreenshot.Texture = isScreenshot ? texture : null;
        boxScreenshot.Visible = isScreenshot && texture != null;
    }

    public void SetCover(Texture2D texture)
    {
        cover.Texture = texture;
        coverPlaceholder.Visible = texture == null;
    }

    public void ClearThumbnails()
    {
        thumbnails.Clear();
    }

    public void AddThumbnail(Texture2D texture)
    {
        var thumbnail = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            CustomMinimumSize = ThumbnailSize
        };

        var ring = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        ring.AddThemeStyleboxOverride("panel", ringStyle);
        Fill(ring, thumbnail);

        strip.AddChild(thumbnail);
        thumbnails.Add((thumbnail, ring));
    }

    public void SelectThumbnail(int index)
    {
        ringStyle.BorderColor = Appearance.Accent;
        ringStyle.SetBorderWidthAll(ThumbnailRingWidth);

        for (int position = 0; position < thumbnails.Count; position++)
        {
            thumbnails[position].Ring.Visible = position == index;
        }
    }

    // One line under the hero image, or a list of rows beside the box. Empty facts are left out.
    public void SetFacts(string status, bool statusIsGood, IReadOnlyList<(string Label, string Value)> facts)
    {
        metaStatus.Visible = !string.IsNullOrEmpty(status);
        metaStatus.Text = status ?? "";
        metaStatus.ThemeTypeVariation = statusIsGood ? "AccentLabel" : "MutedLabel";

        var values = new List<string>();

        foreach (var (label, value) in facts)
        {
            if (label != "Size" && label != "Emulator" && !string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        metaDetails.Text = string.Join("  ·  ", values);

        foreach (Node child in boxFacts.GetChildren())
        {
            boxFacts.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var (label, value) in facts)
        {
            if (!string.IsNullOrEmpty(value))
            {
                AddFactRow(label, value, "DetailValueLabel");
            }
        }

        if (!string.IsNullOrEmpty(status))
        {
            AddFactRow("Status", status, statusIsGood ? "AccentLabel" : "DetailValueLabel");
        }
    }

    private void AddFactRow(string label, string value, string valueVariation)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        row.AddChild(new Label { ThemeTypeVariation = "CaptionLabel", Text = label });

        var valueLabel = new Label
        {
            ThemeTypeVariation = valueVariation,
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis
        };
        valueLabel.AddThemeFontSizeOverride("font_size", FactFontSize);
        row.AddChild(valueLabel);
        boxFacts.AddChild(row);
    }
}
