using Godot;

public partial class GameCard : Control, ICarouselItem, IGameListItem
{
    private const float CardPadding = 12.0f;
    private const float RevealDuration = 0.18f;

    private static readonly ShaderMaterial MicaMaterial =
        GD.Load<ShaderMaterial>("res://assets/materials/mica_panel.tres");

    private Control body;
    private PanelContainer cardPanel;
    private TextureRect coverRect;
    private Label captionLabel;
    private Label titleLabel;
    private TextureRect installedIcon;
    private Panel focusPanel;

    private Control Body => body ??= GetNode<Control>("Body");
    private PanelContainer CardPanel => cardPanel ??= GetNode<PanelContainer>("Body/CardPanel");
    private TextureRect CoverRect => coverRect ??= GetNode<TextureRect>("Body/CardPanel/Content/Stack/Cover");
    private Label CaptionLabel => captionLabel ??= GetNode<Label>("Body/CardPanel/Content/Stack/Caption");
    private Label TitleLabel => titleLabel ??= GetNode<Label>("Body/TitleLabel");
    private TextureRect InstalledIcon => installedIcon ??= GetNode<TextureRect>("Body/InstalledIcon");
    private Panel FocusPanel => focusPanel ??= GetNode<Panel>("Body/FocusPanel");

    public Texture2D Cover => CoverRect.Texture;

    public bool HasRealCover { get; private set; }

    public bool ShowsCover => true;

    public bool CaptionEnabled { get; set; } = true;

    // The carousel marks its selected card on the card itself. The grid draws a selection indicator
    // around the card instead, so its cards keep only the shadow.
    public bool RingWhenSelected { get; set; } = true;

    public float FrameCoverAspect { get; set; }

    public float TextureAspect
    {
        get
        {
            Texture2D texture = CoverRect.Texture;
            return texture != null && texture.GetSize().X > 0 ? texture.GetSize().Y / texture.GetSize().X : 0.0f;
        }
    }

    public void SetCover(Texture2D texture, bool isPlaceholder)
    {
        HasRealCover = !isPlaceholder && texture != null;
        CoverRect.Texture = texture;

        TitleLabel.Visible = !HasRealCover;
        CaptionLabel.Visible = HasRealCover && CaptionEnabled;
    }

    public void Reveal()
    {
        if (revealed)
        {
            return;
        }
        revealed = true;

        if (!IsInsideTree())
        {
            SetBodyAlpha(1.0f);
            return;
        }

        SetBodyAlpha(0.0f);
        revealTween = CreateTween().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        revealTween.TweenProperty(Body, "modulate:a", 1.0f, RevealDuration);
    }

    private void SetBodyAlpha(float alpha)
    {
        if (revealTween != null && revealTween.IsValid())
        {
            revealTween.Kill();
        }
        revealTween = null;

        Color modulate = Body.Modulate;
        modulate.A = alpha;
        Body.Modulate = modulate;
    }

    public void ResetReveal()
    {
        revealed = false;
        SetBodyAlpha(0.0f);
    }

    private bool revealed;
    private Tween revealTween;

    public string Title
    {
        set
        {
            TitleLabel.Text = value;
            CaptionLabel.Text = value;
        }
    }

    private bool selected;

    public bool Selected
    {
        get => selected;
        set
        {
            selected = value;
            FocusPanel.Visible = value;
            CardPanel.Material = value ? MicaMaterial : null;

            if (value && !focusStyleChosen)
            {
                focusStyleChosen = true;
                EnsureSharedStyles();
                if (RingWhenSelected)
                {
                    FocusPanel.AddThemeStyleboxOverride("panel", ringFocusStyle);
                }
            }
        }
    }

    private bool focusStyleChosen;

    private const int CardCornerRadius = 8;
    private const int SimpleRingWidth = 4;
    private const int SimpleRingGap = FocusHighlight.RingGap;

    // The scene's styles are shared by every card, so recolouring them once recolours all cards.
    private static StyleBoxFlat cardStyle;
    private static StyleBoxFlat shadowFocusStyle;
    private static StyleBoxFlat ringFocusStyle;

    private void EnsureSharedStyles()
    {
        if (cardStyle != null)
        {
            return;
        }

        cardStyle = CardPanel.GetThemeStylebox("panel") as StyleBoxFlat;
        shadowFocusStyle = FocusPanel.GetThemeStylebox("panel") as StyleBoxFlat;
        ringFocusStyle = shadowFocusStyle?.Duplicate() as StyleBoxFlat;
        Appearance.Changed += RestyleSharedStyles;
        RestyleSharedStyles();
    }

    public override void _Ready()
    {
        EnsureSharedStyles();
    }

    // Simple draws an accent ring a little outside the selected carousel card.
    private static void RestyleSharedStyles()
    {
        if (cardStyle != null)
        {
            cardStyle.BgColor = new Color(0, 0, 0, Appearance.IsLight ? 0.07f : 0.25f);
        }

        Color shadow = new Color(0, 0, 0, Appearance.IsLight ? 0.2f : 0.45f);

        if (shadowFocusStyle != null)
        {
            shadowFocusStyle.ShadowColor = shadow;
        }

        if (ringFocusStyle != null)
        {
            bool ring = Appearance.IsSimple;
            ringFocusStyle.ShadowColor = shadow;
            ringFocusStyle.BorderColor = Appearance.Accent;
            ringFocusStyle.SetBorderWidthAll(ring ? SimpleRingWidth : 0);
            ringFocusStyle.SetExpandMarginAll(ring ? SimpleRingGap : 0);
            ringFocusStyle.SetCornerRadiusAll(ring ? CardCornerRadius + SimpleRingGap : CardCornerRadius);
        }
    }

    public void SetInstalledIcon(Texture2D icon)
    {
        InstalledIcon.Texture = icon;
        InstalledIcon.Visible = icon != null;
    }

    public float CoverAspectRatio
    {
        get
        {
            float coverAspect = FrameCoverAspect > 0.0f ? FrameCoverAspect : TextureAspect;

            if (coverAspect <= 0.0f)
            {
                return 0.0f;
            }

            float cardWidth = CustomMinimumSize.X;

            if (cardWidth <= 0.0f)
            {
                return coverAspect;
            }

            float innerWidth = Mathf.Max(cardWidth - (2.0f * CardPadding), 1.0f);
            float captionHeight = CaptionEnabled
                ? CaptionLabel.GetCombinedMinimumSize().Y + CaptionLabel.GetParent<BoxContainer>().GetThemeConstant("separation")
                : 0.0f;
            float cardHeight = (innerWidth * coverAspect) + (2.0f * CardPadding) + captionHeight;

            return cardHeight / cardWidth;
        }
    }
}
