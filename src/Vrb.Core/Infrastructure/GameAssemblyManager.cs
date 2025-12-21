using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;

namespace Vrb.Infrastructure;

/// <summary>
/// Manages the loading and resolution of Space Engineers 2 game assemblies.
/// 
/// This class is responsible for:
/// <list type="bullet">
///   <item><description>Loading required game DLLs in the correct order</description></item>
///   <item><description>Maintaining a registry of loaded assemblies</description></item>
///   <item><description>Providing assembly resolution for runtime dependency loading</description></item>
/// </list>
/// 
/// The game uses many interdependent assemblies, so load order matters.
/// Priority assemblies (VRage.Library, VRage.DCS, etc.) are loaded first.
/// </summary>
public static class GameAssemblyManager
{
    /// <summary>
    /// List of assemblies that have been successfully loaded.
    /// </summary>
    private static readonly List<Assembly> _loadedAssemblies = new();
    
    /// <summary>
    /// Path to the game's binary directory (contains DLLs).
    /// </summary>
    private static string? _binPath;

    /// <summary>
    /// Gets the read-only list of all loaded game assemblies.
    /// </summary>
    public static IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;

    /// <summary>
    /// Loads all required game assemblies from the specified game installation path.
    /// </summary>
    /// <param name="gamePath">Root path of the Space Engineers 2 installation.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <remarks>
    /// This method:
    /// 1. Searches for VRage.Library.dll to find the binary directory
    /// 2. Loads priority assemblies first (VRage.Library, VRage.DCS, etc.)
    /// 3. Loads remaining DLLs (excluding System.* and Microsoft.*)
    /// 4. Registers an assembly resolver for runtime dependency resolution
    /// </remarks>
    public static void LoadAssemblies(string gamePath, ILogger logger)
    {
        logger.LogInformation("Searching for VRage.Library.dll in {GamePath}...", gamePath);

        // Find the binary directory by locating VRage.Library.dll
        var vrageLibFiles = Directory.GetFiles(gamePath, "VRage.Library.dll", SearchOption.AllDirectories);

        if (vrageLibFiles.Length == 0)
        {
            logger.LogError("Could not find VRage.Library.dll in the specified game path.");
            return;
        }

        _binPath = Path.GetDirectoryName(vrageLibFiles[0]);
        if (_binPath == null) return;

        logger.LogInformation("Found binaries in: {BinPath}", _binPath);

        var dlls = Directory.GetFiles(_binPath, "*.dll", SearchOption.TopDirectoryOnly);

        // Priority assemblies must be loaded first due to dependencies
        string[] priorityDlls =
        [
            "VRage.Library.dll",      // Core library types
            "VRage.DCS.dll",          // Entity/Component system
            "VRage.Core.dll",         // Core game types
            "VRage.Core.Game.dll",    // Game-specific core (EntityBundle lives here)
            "VRage.Game.dll",         // Game framework
            "Game2.Simulation.dll",   // SE2 simulation (SessionComponentsSnapshot)
            "Game2.Game.dll"          // SE2 game (AssetJournal)
        ];

        // Load priority assemblies first
        foreach (var priorityDll in priorityDlls)
        {
            LoadAssembly(Path.Combine(_binPath, priorityDll), priorityDll, logger);
        }

        // Load remaining assemblies (skip system/Microsoft assemblies)
        foreach (var dll in dlls)
        {
            var fileName = Path.GetFileName(dll);
            if (priorityDlls.Contains(fileName)) continue;
            if (fileName.StartsWith("System.") || fileName.StartsWith("Microsoft.")) continue;

            try
            {
                var asm = Assembly.LoadFrom(dll);
                _loadedAssemblies.Add(asm);
            }
            catch (Exception ex)
            {
                // Trace-level logging for non-critical load failures
                logger.LogTrace(ex, "Skipped loading assembly {FileName}", fileName);
            }
        }

        // Register resolver for runtime assembly resolution
        RegisterAssemblyResolver(logger);
    }

    /// <summary>
    /// Gets a loaded assembly by its simple name.
    /// </summary>
    /// <param name="name">Simple assembly name (e.g., "VRage.Library").</param>
    /// <returns>The loaded assembly, or null if not found.</returns>
    public static Assembly? GetAssembly(string name)
    {
        return _loadedAssemblies.FirstOrDefault(a => a.GetName().Name == name);
    }

    /// <summary>
    /// Attempts to load a single assembly from the specified path.
    /// </summary>
    private static void LoadAssembly(string fullPath, string fileName, ILogger logger)
    {
        if (File.Exists(fullPath))
        {
            try
            {
                var asm = Assembly.LoadFrom(fullPath);
                
                // Avoid duplicate loading
                if (_loadedAssemblies.All(a => a.FullName != asm.FullName))
                {
                    _loadedAssemblies.Add(asm);
                    logger.LogInformation("Loaded: {FileName}", fileName);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load {FileName}", fileName);
            }
        }
        else
        {
            logger.LogWarning("File not found: {FullPath}", fullPath);
        }
    }

    /// <summary>
    /// Registers an assembly resolver to handle runtime assembly resolution requests.
    /// This is necessary because the game's DLLs have complex interdependencies.
    /// </summary>
    private static void RegisterAssemblyResolver(ILogger logger)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name);
            
            // First check if already loaded in the AppDomain
            var existing = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name.Name);
            if (existing != null) return existing;

            // Try to load from the game's binary directory
            if (_binPath != null)
            {
                var assemblyPath = Path.Combine(_binPath, name.Name + ".dll");
                if (File.Exists(assemblyPath))
                {
                    try
                    {
                        return Assembly.LoadFrom(assemblyPath);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            return null;
        };
        
        logger.LogDebug("Registered AssemblyResolver.");
    }
}
