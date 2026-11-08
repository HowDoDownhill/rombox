using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

// Matches a ROM's file name against the names in a libretro thumbnail folder. Those follow No-Intro
// and Redump naming, and a user's files often differ by region tags, punctuation or a moved "The".
public static class LibretroThumbnailMatcher
{
    private const string ReplacedCharacters = "&*/:`<>?\\|";

    private static readonly Regex Tags = new Regex(@"[\(\[]([^\)\]]*)[\)\]]", RegexOptions.Compiled);
    private static readonly Regex TrailingArticle = new Regex(@"^(.+?), (The|A|An)\b(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> PrereleaseTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "beta", "proto", "prototype", "demo", "sample", "kiosk", "pirate", "program", "promo"
    };

    private static readonly string[] PreferredRegions = { "usa", "world", "europe", "japan" };

    public sealed class Index
    {
        public HashSet<string> Exact { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, List<string>> ByTitle { get; } = new Dictionary<string, List<string>>();
    }

    public static Index BuildIndex(IEnumerable<string> thumbnailNames)
    {
        var index = new Index();

        foreach (string name in thumbnailNames)
        {
            index.Exact.Add(name);
            string key = TitleKey(name);

            if (key.Length == 0)
            {
                continue;
            }

            if (!index.ByTitle.TryGetValue(key, out List<string> names))
            {
                names = new List<string>();
                index.ByTitle[key] = names;
            }

            names.Add(name);
        }

        return index;
    }

    // The thumbnail name for a ROM, or null when no thumbnail fits it.
    public static string Find(Index index, string romFileNameWithoutExtension)
    {
        string sanitized = Sanitize(romFileNameWithoutExtension);

        if (index.Exact.TryGetValue(sanitized, out string exact))
        {
            return exact;
        }

        if (!index.ByTitle.TryGetValue(TitleKey(romFileNameWithoutExtension), out List<string> candidates))
        {
            return null;
        }

        HashSet<string> romTags = TagsOf(romFileNameWithoutExtension);

        // A retail ROM should not pick up a beta's box, unless the ROM is that beta.
        return candidates
            .Where(name => romTags.Overlaps(PrereleaseTags) || !TagsOf(name).Overlaps(PrereleaseTags))
            .OrderByDescending(name => Score(name, romTags))
            .ThenBy(name => name.Length)
            .ThenBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    // RetroArch replaces these characters with an underscore in thumbnail file names.
    public static string Sanitize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (char character in name)
        {
            builder.Append(ReplacedCharacters.IndexOf(character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    private static int Score(string name, HashSet<string> romTags)
    {
        HashSet<string> tags = TagsOf(name);
        int score = tags.Count(romTags.Contains) * 10;

        for (int index = 0; index < PreferredRegions.Length; index++)
        {
            if (tags.Contains(PreferredRegions[index]))
            {
                score += PreferredRegions.Length - index;
                break;
            }
        }

        return score;
    }

    private static HashSet<string> TagsOf(string name)
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Tags.Matches(name))
        {
            foreach (string tag in match.Groups[1].Value.Split(','))
            {
                tags.Add(tag.Trim().ToLowerInvariant());
            }
        }

        return tags;
    }

    // Letters and digits of the title alone, with "Legend of Zelda, The" read as "The Legend of Zelda".
    private static string TitleKey(string name)
    {
        string title = Tags.Replace(name, "").Trim();
        Match article = TrailingArticle.Match(title);

        if (article.Success)
        {
            title = $"{article.Groups[2].Value} {article.Groups[1].Value}{article.Groups[3].Value}";
        }

        var key = new StringBuilder(title.Length);

        foreach (char character in title)
        {
            if (char.IsLetterOrDigit(character))
            {
                key.Append(char.ToLowerInvariant(character));
            }
        }

        return key.ToString();
    }
}
