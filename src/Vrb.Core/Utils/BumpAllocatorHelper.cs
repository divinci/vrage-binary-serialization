using System.Reflection;
using Vrb.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Vrb.Utils;

internal static class BumpAllocatorHelper
{
    public static void EnsureInitialized(ILogger logger)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            if (vrageLibrary == null) return;

            var bumpAllocatorType = vrageLibrary.GetType("Keen.VRage.Library.Memory.BumpAllocator");
            if (bumpAllocatorType == null) return;

            // BumpAllocator.Instance is a ThreadStatic field
            var instanceField = bumpAllocatorType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField == null) return;

            // Get the current value (struct)
            var instance = instanceField.GetValue(null);
            if (instance == null) return;

            // Check if already initialized
            var initializedProp = bumpAllocatorType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Instance);
            if (initializedProp != null)
            {
                var isAlreadyInitialized = (bool?)initializedProp.GetValue(instance);
                if (isAlreadyInitialized == true)
                {
                    return; // Already initialized on this thread
                }
            }

            // Initialize
            var initMethod = bumpAllocatorType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(instance, null);
                // Set the modified instance back to the static field
                instanceField.SetValue(null, instance);
                // logger.LogDebug("BumpAllocator initialized on thread {ThreadId}", Environment.CurrentManagedThreadId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing BumpAllocator on thread {ThreadId}", Environment.CurrentManagedThreadId);
        }
    }
}

