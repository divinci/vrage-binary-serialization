using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace Vrb.Infrastructure;

public static class GameInstallLocator
{
    private static readonly string[] CommonPaths =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Space Engineers 2",
        @"D:\Steam\steamapps\common\Space Engineers 2",
        @"E:\Steam\steamapps\common\Space Engineers 2",
        @"S:\Steam\steamapps\common\Space Engineers 2"
    ];

    private static readonly string[] GameFolderNames = ["Space Engineers 2", "SpaceEngineers2", "Space Engineers II"];

    public static string? FindGameInstallPath(ILogger logger)
    {
        logger.LogInformation("Attempting to find game install path...");

        // Method 1: Try to find from common steam paths
        foreach (var path in CommonPaths)
        {
            if (Directory.Exists(path))
            {
                logger.LogInformation("Found game path in common locations: {Path}", path);
                return path;
            }
        }

        // Method 2: Read registry
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam");
                if (key?.GetValue("InstallPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
                {
                    // Check main steam apps
                    foreach (var name in GameFolderNames)
                    {
                        var fullPath = Path.Combine(steamPath, @"steamapps\common", name);
                        if (Directory.Exists(fullPath))
                        {
                            logger.LogInformation("Found game path via registry: {Path}", fullPath);
                            return fullPath;
                        }
                    }

                    // Check library folders
                    var libraryVdf = Path.Combine(steamPath, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(libraryVdf))
                    {
                        var libPath = ParseLibraryFolders(libraryVdf, logger);
                        if (libPath != null)
                        {
                            logger.LogInformation("Found game path via libraryfolders.vdf: {Path}", libPath);
                            return libPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error reading registry or parsing Steam config.");
        }

        logger.LogWarning("Could not find game installation path.");
        return null;
    }

    private static string? ParseLibraryFolders(string libraryVdf, ILogger logger)
    {
        try
        {
            var lines = File.ReadAllLines(libraryVdf);
            foreach (var line in lines)
            {
                if (line.Contains("\"path\""))
                {
                    var parts = line.Split('"');
                    if (parts.Length > 3)
                    {
                        var libPath = parts[3].Replace("\\\\", "\\");
                        foreach (var name in GameFolderNames)
                        {
                            var gameLibPath = Path.Combine(libPath, @"steamapps\common", name);
                            if (Directory.Exists(gameLibPath)) return gameLibPath;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse libraryfolders.vdf");
        }
        return null;
    }
}

