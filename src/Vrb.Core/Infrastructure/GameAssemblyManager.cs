using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;

namespace Vrb.Infrastructure;

public static class GameAssemblyManager
{
    private static readonly List<Assembly> _loadedAssemblies = new();
    private static string? _binPath;

    public static IReadOnlyList<Assembly> LoadedAssemblies => _loadedAssemblies;

    public static void LoadAssemblies(string gamePath, ILogger logger)
    {
        logger.LogInformation("Searching for VRage.Library.dll in {GamePath}...", gamePath);

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

        // Core assemblies to load first
        string[] priorityDlls =
        [
            "VRage.Library.dll",
            "VRage.DCS.dll",
            "VRage.Core.dll",
            "VRage.Core.Game.dll",
            "VRage.Game.dll",
            "Game2.Simulation.dll",
            "Game2.Game.dll"
        ];

        foreach (var priorityDll in priorityDlls)
        {
            LoadAssembly(Path.Combine(_binPath, priorityDll), priorityDll, logger);
        }

        // Load others
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
                // Ignore load errors for other assemblies, just trace them
                logger.LogTrace(ex, "Skipped loading assembly {FileName}", fileName);
            }
        }

        RegisterAssemblyResolver(logger);
    }

    public static Assembly? GetAssembly(string name)
    {
        return _loadedAssemblies.FirstOrDefault(a => a.GetName().Name == name);
    }

    private static void LoadAssembly(string fullPath, string fileName, ILogger logger)
    {
        if (File.Exists(fullPath))
        {
            try
            {
                var asm = Assembly.LoadFrom(fullPath);
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

    private static void RegisterAssemblyResolver(ILogger logger)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var name = new AssemblyName(args.Name);
            var existing = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name.Name);
            if (existing != null) return existing;

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

