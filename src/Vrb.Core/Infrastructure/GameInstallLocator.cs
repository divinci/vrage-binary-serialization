using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;

namespace Vrb.Infrastructure;

/// <summary>
/// Locates the Space Engineers 2 game installation directory.
/// 
/// Uses multiple detection methods in order of preference:
/// <list type="number">
///   <item><description>Common Steam installation paths</description></item>
///   <item><description>Windows Registry (Steam install path)</description></item>
///   <item><description>Steam library folders configuration</description></item>
/// </list>
/// </summary>
public static class GameInstallLocator
{
    /// <summary>
    /// Common Steam installation paths to check first.
    /// These cover the most typical installation scenarios.
    /// </summary>
    private static readonly string[] CommonPaths =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\Space Engineers 2",
        @"D:\Steam\steamapps\common\Space Engineers 2",
        @"E:\Steam\steamapps\common\Space Engineers 2",
        @"S:\Steam\steamapps\common\Space Engineers 2"
    ];

    /// <summary>
    /// Possible folder names for the game installation.
    /// Different versions or locales may use different names.
    /// </summary>
    private static readonly string[] GameFolderNames = 
    [
        "Space Engineers 2", 
        "SpaceEngineers2", 
        "Space Engineers II"
    ];

    /// <summary>
    /// Attempts to find the Space Engineers 2 installation path.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>The game installation path, or null if not found.</returns>
    public static string? FindGameInstallPath(ILogger logger)
    {
        logger.LogInformation("Attempting to find game install path...");

        // Method 1: Check common Steam installation paths
        foreach (var path in CommonPaths)
        {
            if (Directory.Exists(path))
            {
                logger.LogInformation("Found game path in common locations: {Path}", path);
                return path;
            }
        }

        // Method 2: Use Windows Registry to find Steam, then search for game
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Valve\Steam");
                if (key?.GetValue("InstallPath") is string steamPath && !string.IsNullOrEmpty(steamPath))
                {
                    // Check the main Steam apps folder
                    foreach (var name in GameFolderNames)
                    {
                        var fullPath = Path.Combine(steamPath, @"steamapps\common", name);
                        if (Directory.Exists(fullPath))
                        {
                            logger.LogInformation("Found game path via registry: {Path}", fullPath);
                            return fullPath;
                        }
                    }

                    // Method 3: Parse Steam library folders configuration
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

    /// <summary>
    /// Parses the Steam libraryfolders.vdf file to find additional library locations.
    /// </summary>
    /// <param name="libraryVdf">Path to the libraryfolders.vdf file.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>The game path if found in a library folder, null otherwise.</returns>
    private static string? ParseLibraryFolders(string libraryVdf, ILogger logger)
    {
        try
        {
            var lines = File.ReadAllLines(libraryVdf);
            foreach (var line in lines)
            {
                // Look for "path" entries in the VDF file
                if (line.Contains("\"path\""))
                {
                    var parts = line.Split('"');
                    if (parts.Length > 3)
                    {
                        // VDF uses escaped backslashes
                        var libPath = parts[3].Replace("\\\\", "\\");
                        
                        // Check if game exists in this library
                        foreach (var name in GameFolderNames)
                        {
                            var gameLibPath = Path.Combine(libPath, @"steamapps\common", name);
                            if (Directory.Exists(gameLibPath)) 
                                return gameLibPath;
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
