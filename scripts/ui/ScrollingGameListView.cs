using Godot;
using System.Collections.Generic;

public abstract partial class ScrollingGameListView : GameListView
{
    private const float ScrollRate = 16.0f;
    private const float SettleDistance = 0.5f;
    private const float MaximumGlideScreens = 2.0f;
    private const float FlingFriction = 3.5f;
    private const float MinimumFlingSpeed = 40.0f;
    private const float MaximumFlingSpeed = 8000.0f;

    private readonly Control content = new Control { Name = "Content", MouseFilter = MouseFilterEnum.Ignore };
    private float scrollOffset;
    private float targetScrollOffset;
    private bool followSelection = true;
    private float flingVelocity;

    protected override Control CardParent => content;

    public override bool IsAnimating => flingVelocity != 0.0f || Mathf.Abs(scrollOffset - targetScrollOffset) > SettleDistance;

    private float MaximumScroll => Mathf.Max(0.0f, ContentHeight - Size.Y);

    protected abstract float ContentHeight { get; }

    protected abstract float ResolveTargetScroll(float currentScroll);

    protected abstract void ResolveVisibleItems(float fromScroll, float toScroll, HashSet<int> visibleItems);

    protected abstract void PlaceCard(Control card, int itemIndex);

    protected abstract float SelectionHighlightGrow { get; }

    // How far outside the selected item the Simple accent ring sits.
    protected abstract float SelectionRingGrow { get; }

    public override void _Ready()
    {
        base._Ready();
        AddChild(content);
        SetProcess(false);
    }

    protected virtual void PrepareLayout()
    {
    }

    public override void UpdateLayout(bool animated = true)
    {
        if (ItemCount == 0 || Size.X <= 0.0f || Size.Y <= 0.0f)
        {
            ReleaseAllCards();
            scrollOffset = 0.0f;
            targetScrollOffset = 0.0f;
            content.Position = Vector2.Zero;

            if (ItemCount > 0)
            {
                NotifyItemFocused();
            }

            return;
        }

        PrepareLayout();

        float maximumScroll = MaximumScroll;
        targetScrollOffset = Mathf.Clamp(followSelection ? ResolveTargetScroll(targetScrollOffset) : targetScrollOffset, 0.0f, maximumScroll);

        if (!animated || Mathf.Abs(targetScrollOffset - scrollOffset) > Size.Y * MaximumGlideScreens)
        {
            scrollOffset = targetScrollOffset;
        }

        scrollOffset = Mathf.Clamp(scrollOffset, 0.0f, maximumScroll);

        BindCardsBetween(Mathf.Min(scrollOffset, targetScrollOffset), Mathf.Max(scrollOffset, targetScrollOffset) + Size.Y, true);
        content.Position = new Vector2(0.0f, -scrollOffset);
        SetProcess(IsAnimating);

        if (BoundCards.TryGetValue(SelectedIndex, out Control selectedCard))
        {
            SelectionHighlighter.Set(selectedCard, selectedCard, true, SelectionHighlightGrow, SelectionRingGrow);
        }

        NotifyItemFocused();
    }

    private void BindCardsBetween(float fromScroll, float toScroll, bool placeEveryCard)
    {
        var visibleItems = new HashSet<int>();
        ResolveVisibleItems(fromScroll, toScroll, visibleItems);
        visibleItems.Add(SelectedIndex);
        ReleaseCardsOutside(visibleItems);

        foreach (int itemIndex in visibleItems)
        {
            if (TryGetOrAcquireCard(itemIndex, out Control card, out bool isNewlyBound) && (placeEveryCard || isNewlyBound))
            {
                PlaceCard(card, itemIndex);
            }
        }
    }

    protected override void OnSelectionDriven()
    {
        followSelection = true;
        flingVelocity = 0.0f;
    }

    protected override void OnPointerPressed()
    {
        flingVelocity = 0.0f;
    }

    protected override void OnDragMoved(float scrollDelta, float scrollVelocity)
    {
        followSelection = false;
        flingVelocity = 0.0f;
        ScrollFreely(scrollDelta);
    }

    protected override void OnDragReleased(float scrollVelocity)
    {
        flingVelocity = Mathf.Abs(scrollVelocity) < MinimumFlingSpeed ? 0.0f : Mathf.Clamp(scrollVelocity, -MaximumFlingSpeed, MaximumFlingSpeed);
        SetProcess(true);
    }

    private void ScrollFreely(float scrollDelta)
    {
        float previousScroll = scrollOffset;
        targetScrollOffset = Mathf.Clamp(scrollOffset + scrollDelta, 0.0f, MaximumScroll);
        scrollOffset = targetScrollOffset;

        if (scrollOffset == previousScroll && scrollDelta != 0.0f)
        {
            flingVelocity = 0.0f;
        }

        BindCardsBetween(scrollOffset, scrollOffset + Size.Y, false);
        content.Position = new Vector2(0.0f, -scrollOffset);
    }

    public override void _Process(double delta)
    {
        if (flingVelocity != 0.0f)
        {
            ScrollFreely(flingVelocity * (float)delta);
            flingVelocity *= Mathf.Exp(-FlingFriction * (float)delta);

            if (Mathf.Abs(flingVelocity) < MinimumFlingSpeed)
            {
                flingVelocity = 0.0f;
            }

            if (flingVelocity == 0.0f)
            {
                SetProcess(false);
                OnAnimationSettled();
            }

            return;
        }

        float glide = 1.0f - Mathf.Exp(-ScrollRate * (float)delta);
        scrollOffset = Mathf.Lerp(scrollOffset, targetScrollOffset, glide);

        bool settled = !IsAnimating;

        if (settled)
        {
            scrollOffset = targetScrollOffset;
            SetProcess(false);
        }

        content.Position = new Vector2(0.0f, -scrollOffset);

        if (settled)
        {
            OnAnimationSettled();
        }
    }

    protected static float KeepInView(float currentScroll, float itemTop, float itemBottom, float viewHeight, float margin)
    {
        if (itemTop - margin < currentScroll)
        {
            return itemTop - margin;
        }

        if (itemBottom + margin > currentScroll + viewHeight)
        {
            return itemBottom + margin - viewHeight;
        }

        return currentScroll;
    }
}
