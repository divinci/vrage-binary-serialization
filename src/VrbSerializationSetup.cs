using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using vrb.Core;
using vrb.Infrastructure;

namespace vrb;

public static class VrbSerializationSetup
{
    /// <summary>
    /// Registers the necessary VRB services (VrbProcessingService, GameEnvironmentInitializer) into the service collection.
    /// Requires logging to be configured separately.
    /// </summary>
    public static IServiceCollection AddVrbServices(this IServiceCollection services)
    {
        services.AddSingleton<GameEnvironmentInitializer>();
        services.AddSingleton<VrbProcessingService>();
        return services;
    }

    /// <summary>
    /// Performs the necessary initialization steps: finding the game path, loading assemblies, and initializing the game environment.
    /// </summary>
    /// <param name="serviceProvider">The service provider containing registered services.</param>
    /// <param name="gameInstallPath">Optional. Explicit path to the game installation. If null, auto-discovery is attempted.</param>
    /// <exception cref="DirectoryNotFoundException">Thrown if the game installation cannot be found.</exception>
    public static void InitializeVrb(this IServiceProvider serviceProvider, string? gameInstallPath = null)
    {
        // Try to get a logger, fallback to NullLogger if not found/configured (though highly recommended)
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("VrbSerializationSetup") ?? NullLogger.Instance;

        // 1. Find Game Path (if not provided)
        if (string.IsNullOrEmpty(gameInstallPath))
        {
            gameInstallPath = GameInstallLocator.FindGameInstallPath(logger);
        }

        if (string.IsNullOrEmpty(gameInstallPath))
        {
            logger.LogError("Could not find Space Engineers 2 installation path. Initialization aborted.");
            throw new DirectoryNotFoundException("Could not find Space Engineers 2 installation path.");
        }

        logger.LogInformation("Using Game Path: {GamePath}", gameInstallPath);

        // 2. Load Assemblies
        GameAssemblyManager.LoadAssemblies(gameInstallPath, logger);

        // 3. Initialize Game Environment
        var initializer = serviceProvider.GetRequiredService<GameEnvironmentInitializer>();
        initializer.Initialize();
    }
}
