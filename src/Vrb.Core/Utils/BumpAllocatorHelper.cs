using System.Reflection;
using Vrb.Infrastructure;
using Microsoft.Extensions.Logging;

namespace Vrb.Utils;

/// <summary>
/// Helper for managing the VRage BumpAllocator.
/// 
/// The BumpAllocator is a thread-local memory allocator used by the game engine
/// for efficient temporary allocations during serialization. It must be initialized
/// on each thread that performs serialization operations.
/// 
/// This helper ensures the allocator is properly initialized before use.
/// </summary>
internal static class BumpAllocatorHelper
{
    /// <summary>
    /// Ensures the BumpAllocator is initialized for the current thread.
    /// This is idempotent - calling it multiple times on the same thread has no effect.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <remarks>
    /// The BumpAllocator.Instance is a ThreadStatic field, so each thread needs its own
    /// initialization. This method uses reflection to:
    /// 1. Access the static Instance field
    /// 2. Check if it's already initialized
    /// 3. Call Initialize() if needed
    /// 4. Write the modified struct back to the static field
    /// </remarks>
    public static void EnsureInitialized(ILogger logger)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            if (vrageLibrary == null) return;

            var bumpAllocatorType = vrageLibrary.GetType("Keen.VRage.Library.Memory.BumpAllocator");
            if (bumpAllocatorType == null) return;

            // BumpAllocator.Instance is a ThreadStatic field (struct)
            var instanceField = bumpAllocatorType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField == null) return;

            // Get the current value (this is a struct, so we get a copy)
            var instance = instanceField.GetValue(null);
            if (instance == null) return;

            // Check if already initialized to avoid redundant work
            var initializedProp = bumpAllocatorType.GetProperty("Initialized", BindingFlags.Public | BindingFlags.Instance);
            if (initializedProp != null)
            {
                var isAlreadyInitialized = (bool?)initializedProp.GetValue(instance);
                if (isAlreadyInitialized == true)
                {
                    return; // Already initialized on this thread
                }
            }

            // Initialize the allocator
            var initMethod = bumpAllocatorType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(instance, null);
                
                // IMPORTANT: Since BumpAllocator is a struct, we must write the
                // modified instance back to the static field
                instanceField.SetValue(null, instance);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing BumpAllocator on thread {ThreadId}", 
                Environment.CurrentManagedThreadId);
        }
    }
}
