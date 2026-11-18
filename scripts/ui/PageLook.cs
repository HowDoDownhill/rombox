using Godot;

// The look of the panels RomBox builds in its scenes. Bars run to the window edge with a wash of
// the palette's background colour. Panels inside a page are flat. Both follow the palette, because
// FishUI recolours the shared styles in place.
public static class PageLook
{
    public static void Bar(Node root, string path)
    {
        root.GetNode<Control>(path).AddThemeStyleboxOverride("panel", Appearance.ChromeStyle);
    }

    public static void Surface(Node root, string path)
    {
        root.GetNode<Control>(path).AddThemeStyleboxOverride("panel", Appearance.SurfaceStyle);
    }
}
