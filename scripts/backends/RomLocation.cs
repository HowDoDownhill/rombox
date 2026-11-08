using System.IO;

// Where a game's file sits on this machine. A local library game carries its own path. A RomM
// game downloads to RomsPath/<system slug>/<file name>.
public static class RomLocation
{
    public static string For(ConfigManager configManager, Game game)
    {
        if (game == null)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(game.LocalPath))
        {
            return game.LocalPath;
        }

        string systemSlug = game.System?.Slug ?? game.PlatformSlug;

        if (game.Files == null || game.Files.Count == 0 || string.IsNullOrEmpty(systemSlug))
        {
            return null;
        }

        return Path.GetFullPath(Path.Combine(configManager.RomsPath, systemSlug, game.Files[0].FileName));
    }
}
