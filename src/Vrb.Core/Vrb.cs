using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vrb.Core;

namespace Vrb.Core;

public static class Vrb
{
    private static IServiceProvider? _serviceProvider;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets the initialized VrbProcessingService. Returns null if Initialize() has not been called.
    /// </summary>
    public static VrbProcessingService? Service => _serviceProvider?.GetService<VrbProcessingService>();

    /// <summary>
    /// Initializes the VRB library, setting up internal services and the game environment.
    /// This method is idempotent; calling it multiple times has no effect after the first successful call.
    /// </summary>
    /// <param name="gamePath">Optional path to the Space Engineers installation. If null, auto-discovery is attempted.</param>
    /// <param name="configureLogging">Optional action to configure logging. Defaults to Console logging if null.</param>
    public static void Initialize(string? gamePath = null, Action<ILoggingBuilder>? configureLogging = null)
    {
        if (_serviceProvider != null) return;

        lock (_lock)
        {
            if (_serviceProvider != null) return;

            var services = new ServiceCollection();

            if (configureLogging != null)
            {
                services.AddLogging(configureLogging);
            }
            else
            {
                // Default to basic logging services (no providers = NullLogger)
                services.AddLogging();
            }

            services.AddVrbServices();

            var provider = services.BuildServiceProvider();
            
            // Initialize the environment
            provider.InitializeVrb(gamePath);

            _serviceProvider = provider;
        }
    }
}
