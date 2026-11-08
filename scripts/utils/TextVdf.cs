using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

// Steam's text KeyValues format, as in localconfig.vdf. Reading and writing keep every key in order,
// so a file Steam wrote comes back out with only the values the app changed.
public class TextVdfNode
{
    public string Key;
    public string Value;
    public List<TextVdfNode> Children;

    public bool IsMap => Children != null;

    public static TextVdfNode Map(string key) => new TextVdfNode { Key = key, Children = new List<TextVdfNode>() };

    public TextVdfNode Child(string key)
    {
        return Children?.FirstOrDefault(child => string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    public TextVdfNode GetOrAddMap(string key)
    {
        TextVdfNode existing = Child(key);

        if (existing != null && existing.IsMap)
        {
            return existing;
        }

        if (existing != null)
        {
            Children.Remove(existing);
        }

        TextVdfNode created = Map(key);
        Children.Add(created);
        return created;
    }

    public void SetValue(string key, string value)
    {
        TextVdfNode existing = Child(key);

        if (existing != null && !existing.IsMap)
        {
            existing.Value = value;
            return;
        }

        if (existing != null)
        {
            Children.Remove(existing);
        }

        Children.Add(new TextVdfNode { Key = key, Value = value });
    }
}

public static class TextVdf
{
    public static List<TextVdfNode> Parse(string text)
    {
        int position = 0;
        List<TextVdfNode> nodes = ParseChildren(text, ref position, false);
        return nodes;
    }

    public static string Write(IEnumerable<TextVdfNode> nodes)
    {
        var builder = new StringBuilder();
        WriteChildren(builder, nodes, 0);
        return builder.ToString();
    }

    private static List<TextVdfNode> ParseChildren(string text, ref int position, bool insideMap)
    {
        var children = new List<TextVdfNode>();

        while (true)
        {
            string key = ReadToken(text, ref position, out bool keyWasQuoted);

            if (key == null)
            {
                if (insideMap)
                {
                    throw new FormatException("The file ended inside a block.");
                }

                return children;
            }

            if (key == "}" && !keyWasQuoted)
            {
                if (!insideMap)
                {
                    throw new FormatException($"Unexpected '}}' at character {position}.");
                }

                return children;
            }

            string value = ReadToken(text, ref position, out bool valueWasQuoted);

            if (value == null)
            {
                throw new FormatException($"Key \"{key}\" has no value.");
            }

            if (value == "{" && !valueWasQuoted)
            {
                children.Add(new TextVdfNode { Key = key, Children = ParseChildren(text, ref position, true) });
            }

            else
            {
                children.Add(new TextVdfNode { Key = key, Value = value });
            }

            SkipConditional(text, ref position);
        }
    }

    // Platform conditionals such as [$WIN32] may follow a value. The app never writes them and
    // localconfig.vdf has none, so they are read past and dropped.
    private static void SkipConditional(string text, ref int position)
    {
        int lookahead = position;
        SkipWhitespaceAndComments(text, ref lookahead);

        if (lookahead < text.Length && text[lookahead] == '[')
        {
            int closing = text.IndexOf(']', lookahead);
            position = closing < 0 ? text.Length : closing + 1;
        }
    }

    private static string ReadToken(string text, ref int position, out bool wasQuoted)
    {
        wasQuoted = false;
        SkipWhitespaceAndComments(text, ref position);

        if (position >= text.Length)
        {
            return null;
        }

        char first = text[position];

        if (first == '{' || first == '}')
        {
            position++;
            return first.ToString();
        }

        var token = new StringBuilder();

        if (first == '"')
        {
            wasQuoted = true;
            position++;

            while (position < text.Length && text[position] != '"')
            {
                char character = text[position++];

                if (character == '\\' && position < text.Length)
                {
                    char escaped = text[position++];
                    token.Append(escaped switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        _ => escaped
                    });
                    continue;
                }

                token.Append(character);
            }

            position++;
            return token.ToString();
        }

        while (position < text.Length && !char.IsWhiteSpace(text[position]) && text[position] != '{' && text[position] != '}' && text[position] != '"')
        {
            token.Append(text[position++]);
        }

        return token.ToString();
    }

    private static void SkipWhitespaceAndComments(string text, ref int position)
    {
        while (position < text.Length)
        {
            if (char.IsWhiteSpace(text[position]) || text[position] == '\uFEFF')
            {
                position++;
            }

            else if (text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/')
            {
                while (position < text.Length && text[position] != '\n')
                {
                    position++;
                }
            }

            else
            {
                return;
            }
        }
    }

    private static void WriteChildren(StringBuilder builder, IEnumerable<TextVdfNode> nodes, int depth)
    {
        string indent = new string('\t', depth);

        foreach (TextVdfNode node in nodes)
        {
            if (node.IsMap)
            {
                builder.Append(indent).Append(Quote(node.Key)).Append('\n');
                builder.Append(indent).Append("{\n");
                WriteChildren(builder, node.Children, depth + 1);
                builder.Append(indent).Append("}\n");
            }

            else
            {
                builder.Append(indent).Append(Quote(node.Key)).Append("\t\t").Append(Quote(node.Value ?? "")).Append('\n');
            }
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";
    }
}
