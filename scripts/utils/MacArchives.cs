using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

public static class MacArchives
{
    private const string DittoPath = "/usr/bin/ditto";
    private const string TarPath = "/usr/bin/tar";
    private const string DiskImageToolPath = "/usr/bin/hdiutil";
    private const int DiskImageTrailerLength = 512;

    private enum ArchiveFormat
    {
        Zip,
        DiskImage,
        Other
    }

    public static async Task<bool> Extract(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        switch (DetectFormat(archivePath))
        {
            case ArchiveFormat.DiskImage:
                return await ExtractDiskImage(archivePath, destinationDirectory);

            case ArchiveFormat.Zip:
                return await Run(DittoPath, "-x", "-k", archivePath, destinationDirectory);

            default:
                return await Run(TarPath, "-xf", archivePath, "-C", destinationDirectory);
        }
    }

    public static async Task<bool> Copy(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        return await Run(DittoPath, sourceDirectory, destinationDirectory);
    }

    public static async Task<bool> InstallArchive(string archivePath, string emulatorTargetDirectory, string extractFolderRegex, string stagingDirectory, List<string> preservedRelativePaths)
    {
        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, true);
        }

        try
        {
            if (!await Extract(archivePath, stagingDirectory))
            {
                return false;
            }

            string contentRoot = string.IsNullOrEmpty(extractFolderRegex)
                ? stagingDirectory
                : UniversalInstaller.FindExtractedFolder(stagingDirectory, extractFolderRegex) ?? stagingDirectory;

            Directory.CreateDirectory(emulatorTargetDirectory);

            if (string.IsNullOrEmpty(extractFolderRegex))
            {
                RemoveAppBundles(emulatorTargetDirectory);
            }

            else
            {
                UniversalInstaller.ClearDirectoryPreservingPaths(emulatorTargetDirectory, preservedRelativePaths);
            }

            return await Copy(contentRoot, emulatorTargetDirectory);
        }

        finally
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
    }

    private static void RemoveAppBundles(string directory)
    {
        foreach (string bundlePath in Directory.GetDirectories(directory, "*.app"))
        {
            GD.Print($"[macOS] replacing {bundlePath}");
            Directory.Delete(bundlePath, true);
        }
    }

    private static ArchiveFormat DetectFormat(string archivePath)
    {
        using var archiveStream = File.OpenRead(archivePath);
        var header = new byte[4];

        if (archiveStream.Read(header, 0, header.Length) == header.Length && header[0] == (byte)'P' && header[1] == (byte)'K')
        {
            return ArchiveFormat.Zip;
        }

        if (archiveStream.Length >= DiskImageTrailerLength)
        {
            archiveStream.Seek(-DiskImageTrailerLength, SeekOrigin.End);
            var trailer = new byte[4];

            if (archiveStream.Read(trailer, 0, trailer.Length) == trailer.Length && Encoding.ASCII.GetString(trailer) == "koly")
            {
                return ArchiveFormat.DiskImage;
            }
        }

        return ArchiveFormat.Other;
    }

    private static async Task<bool> ExtractDiskImage(string diskImagePath, string destinationDirectory)
    {
        string mountPoint = Path.Combine(Path.GetTempPath(), "rombox-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mountPoint);

        if (!await Run(DiskImageToolPath, "attach", diskImagePath, "-nobrowse", "-readonly", "-noautoopen", "-noverify", "-mountpoint", mountPoint))
        {
            TryDeleteEmptyDirectory(mountPoint);
            return false;
        }

        try
        {
            foreach (string entryPath in Directory.EnumerateFileSystemEntries(mountPoint))
            {
                string entryName = Path.GetFileName(entryPath);

                if (entryName.StartsWith(".") || new FileInfo(entryPath).LinkTarget != null)
                {
                    continue;
                }

                if (!await Run(DittoPath, entryPath, Path.Combine(destinationDirectory, entryName)))
                {
                    return false;
                }
            }

            return true;
        }

        finally
        {
            await Run(DiskImageToolPath, "detach", mountPoint, "-force");
            TryDeleteEmptyDirectory(mountPoint);
        }
    }

    private static void TryDeleteEmptyDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[macOS] could not remove {directory}: {exception.Message}");
        }
    }

    private static async Task<bool> Run(string toolPath, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(toolPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);

            try
            {
                if (toolPath == DiskImageToolPath)
                {
                    await process.StandardInput.WriteLineAsync("Y");
                }

                process.StandardInput.Close();
            }

            catch (IOException)
            {
            }

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                GD.PrintErr($"[macOS] {Path.GetFileName(toolPath)} {string.Join(" ", arguments)} failed with exit code {process.ExitCode}: {(await standardError).Trim()} {(await standardOutput).Trim()}");
                return false;
            }

            return true;
        }

        catch (Exception exception)
        {
            GD.PrintErr($"[macOS] could not run {toolPath}: {exception.Message}");
            return false;
        }
    }
}
