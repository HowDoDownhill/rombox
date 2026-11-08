using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public static class HostPlatform
{
    public const string ArchitecturePlaceholder = "{arch}";

    public static string OperatingSystemKey => OS.GetName().ToLower();

    public static bool IsMacOS => OperatingSystem.IsMacOS();

    public static string ArchitectureKey => RuntimeInformation.OSArchitecture switch
    {
        Architecture.Arm64 => "arm64",
        Architecture.X64 => "x86_64",
        Architecture.Arm => "arm32",
        Architecture.X86 => "x86_32",
        _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
    };

    public static IEnumerable<string> RecipeKeysFor(string operatingSystem)
    {
        yield return $"{operatingSystem}-{ArchitectureKey}";
        yield return operatingSystem;
    }

    public static InstallRecipe ResolveInstallRecipe(EmulatorMeta emulatorMetadata, string operatingSystem)
    {
        if (emulatorMetadata?.InstallRecipe == null)
        {
            return null;
        }

        foreach (string recipeKey in RecipeKeysFor(operatingSystem))
        {
            if (emulatorMetadata.InstallRecipe.TryGetValue(recipeKey, out InstallRecipe installRecipe) && installRecipe != null)
            {
                return installRecipe;
            }
        }

        return null;
    }

    public static string ApplyArchitecture(string template)
    {
        return string.IsNullOrEmpty(template) ? template : template.Replace(ArchitecturePlaceholder, ArchitectureKey);
    }

    public static string FindExecutableMatching(string installDirectory, string executableRegex)
    {
        if (string.IsNullOrEmpty(executableRegex) || !Directory.Exists(installDirectory))
        {
            return null;
        }

        var executablePattern = new Regex(executableRegex, RegexOptions.IgnoreCase);

        foreach (string candidatePath in EnumerateExecutableCandidates(installDirectory))
        {
            string relativePath = Path.GetRelativePath(installDirectory, candidatePath).Replace('\\', '/');

            if (executablePattern.IsMatch(relativePath))
            {
                return Path.GetFullPath(candidatePath);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateExecutableCandidates(string installDirectory)
    {
        foreach (string filePath in Directory.GetFiles(installDirectory))
        {
            yield return filePath;
        }

        if (!IsMacOS)
        {
            yield break;
        }

        foreach (string bundlePath in Directory.GetDirectories(installDirectory, "*.app").OrderByDescending(Directory.GetLastWriteTimeUtc))
        {
            string bundleExecutableDirectory = Path.Combine(bundlePath, "Contents", "MacOS");

            if (Directory.Exists(bundleExecutableDirectory))
            {
                foreach (string filePath in Directory.GetFiles(bundleExecutableDirectory))
                {
                    yield return filePath;
                }
            }
        }
    }
}
