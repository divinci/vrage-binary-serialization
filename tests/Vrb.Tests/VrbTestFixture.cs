using Microsoft.Extensions.Logging;

namespace Vrb.Tests;

/// <summary>
/// Shared test fixture that initializes the VRB library once for all tests.
/// Provides helper methods for locating test files.
/// </summary>
public class VrbTestFixture : IDisposable
{
    /// <summary>
    /// Indicates whether initialization was successful.
    /// </summary>
    public bool IsInitialized { get; }

    /// <summary>
    /// Error message if initialization failed.
    /// </summary>
    public string? InitializationError { get; }

    public VrbTestFixture()
    {
        try
        {
            // Initialize VRB with console logging for test diagnostics
            Vrb.Core.Vrb.Initialize(configureLogging: builder =>
            {
                builder.ClearProviders();
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            IsInitialized = true;
        }
        catch (Exception ex)
        {
            IsInitialized = false;
            InitializationError = ex.Message;
        }
    }

    /// <summary>
    /// Finds a test file by name, searching common locations.
    /// </summary>
    /// <param name="fileName">The file name to search for (e.g., "savegame.vrb").</param>
    /// <returns>Full path to the file, or null if not found.</returns>
    public string? FindTestFile(string fileName)
    {
        var searchPaths = new[]
        {
            // Local test directory
            Path.Combine(Directory.GetCurrentDirectory(), "Tests", fileName),
            
            // Standard SE2 save location
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpaceEngineers2", "AppData", "SaveGames")
        };

        foreach (var path in searchPaths)
        {
            // Direct file match
            if (File.Exists(path))
                return path;

            // Search recursively in directory
            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, fileName, SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Finds a definitionsets.vrb file from the game installation directory.
    /// </summary>
    /// <returns>Full path to a definitionsets.vrb file, or null if not found.</returns>
    public string? FindDefinitionSetsFile()
    {
        var gamePath = Vrb.Infrastructure.GameInstallLocator.CachedGamePath;
        if (string.IsNullOrEmpty(gamePath))
            return null;

        var knownPaths = new[]
        {
            Path.Combine(gamePath, @"GameData\Vanilla\Content\definitionsets.vrb"),
            Path.Combine(gamePath, @"VRage\GameData\Engine\Content\definitionsets.vrb")
        };

        return knownPaths.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Gets all definitionsets.vrb files from the game installation directory.
    /// </summary>
    /// <returns>Full paths to all definitionsets.vrb files found.</returns>
    public IEnumerable<string> GetAllDefinitionSetsFiles()
    {
        var gamePath = Vrb.Infrastructure.GameInstallLocator.CachedGamePath;
        if (string.IsNullOrEmpty(gamePath))
            return Enumerable.Empty<string>();

        var knownPaths = new[]
        {
            Path.Combine(gamePath, @"GameData\Vanilla\Content\definitionsets.vrb"),
            Path.Combine(gamePath, @"VRage\GameData\Engine\Content\definitionsets.vrb")
        };

        return knownPaths.Where(File.Exists);
    }

    /// <summary>
    /// Gets all save game directories (for testing multiple files).
    /// </summary>
    public IEnumerable<string> GetAllSaveDirectories()
    {
        var savePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpaceEngineers2", "AppData", "SaveGames");

        if (Directory.Exists(savePath))
        {
            return Directory.GetDirectories(savePath);
        }

        return Enumerable.Empty<string>();
    }

    public void Dispose()
    {
        // Cleanup if needed
        GC.SuppressFinalize(this);
    }
}

