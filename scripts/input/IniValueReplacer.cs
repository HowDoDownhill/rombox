using System.Collections.Generic;
using System.IO;

public static class IniValueReplacer
{
    public static int ReplaceInSection(string configurationFilePath, string sectionName, IReadOnlyDictionary<string, string> replacements)
    {
        if (!File.Exists(configurationFilePath))
        {
            return 0;
        }

        string[] lines = File.ReadAllLines(configurationFilePath);
        bool isInsideSection = false;
        int replacedLineCount = 0;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            string trimmedLine = lines[lineIndex].Trim();

            if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
            {
                isInsideSection = trimmedLine.Substring(1, trimmedLine.Length - 2) == sectionName;
                continue;
            }

            int separatorIndex = lines[lineIndex].IndexOf('=');

            if (!isInsideSection || separatorIndex < 0)
            {
                continue;
            }

            string value = lines[lineIndex].Substring(separatorIndex + 1);
            string replacedValue = value;

            foreach (KeyValuePair<string, string> replacement in replacements)
            {
                replacedValue = replacedValue.Replace(replacement.Key, replacement.Value);
            }

            if (replacedValue != value)
            {
                lines[lineIndex] = lines[lineIndex].Substring(0, separatorIndex + 1) + replacedValue;
                replacedLineCount++;
            }
        }

        if (replacedLineCount > 0)
        {
            File.WriteAllLines(configurationFilePath, lines);
        }

        return replacedLineCount;
    }
}
