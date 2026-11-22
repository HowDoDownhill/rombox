using Godot;

public interface IGameListItem
{
    string Title { set; }

    // A short label over the item, such as Recomp. Null or empty hides it.
    string Badge { set; }
    bool Selected { get; set; }
    bool ShowsCover { get; }
    void SetCover(Texture2D texture, bool isPlaceholder);
    void SetInstalledIcon(Texture2D icon);
    void Reveal();
    void ResetReveal();
}
