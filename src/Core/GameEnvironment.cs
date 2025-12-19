using Microsoft.Extensions.Logging;
using System.Collections;
using System.Reflection;
using vrb.Infrastructure;

namespace vrb.Core;

public static class GameEnvironment
{
    public static void Initialize(ILogger logger)
    {
        logger.LogInformation("Initializing Game Environment...");
        InitializeMetadataManager(logger);
        InitializeDefinitionManager(logger);
        InitializeBumpAllocator(logger);
        logger.LogInformation("Game Environment Initialized.");
    }

    private static void InitializeMetadataManager(ILogger logger)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            if (vrageLibrary == null)
            {
                logger.LogWarning("VRage.Library not found. Skipping MetadataManager initialization.");
                return;
            }

            var metadataManagerType = vrageLibrary.GetTypes().FirstOrDefault(t => t.Name == "MetadataManager" && t.Namespace == "Keen.VRage.Library.Reflection");
            if (metadataManagerType == null)
            {
                logger.LogWarning("MetadataManager type not found.");
                return;
            }

            var instanceProp = metadataManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (instanceProp == null)
            {
                logger.LogWarning("MetadataManager.Instance property not found.");
                return;
            }

            var instance = instanceProp.GetValue(null);
            if (instance == null)
            {
                logger.LogWarning("MetadataManager.Instance is null.");
                return;
            }

            var pushContextMethod = metadataManagerType.GetMethod("PushContext", [typeof(IEnumerable<Assembly>)]);

            if (pushContextMethod != null)
            {
                pushContextMethod.Invoke(instance, [GameAssemblyManager.LoadedAssemblies]);
                logger.LogInformation("MetadataManager initialized.");
            }
            else
            {
                logger.LogWarning("MetadataManager.PushContext method not found.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing MetadataManager.");
        }
    }

    private static void InitializeDefinitionManager(ILogger logger)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            var game2Sim = GameAssemblyManager.GetAssembly("Game2.Simulation");

            if (vrageLibrary == null) { logger.LogWarning("VRage.Library not found for DefinitionManager."); return; }
            if (game2Sim == null) { logger.LogWarning("Game2.Simulation not found for DefinitionManager."); return; }

            var definitionManagerType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionManager");
            if (definitionManagerType == null) { logger.LogWarning("Could not find DefinitionManager type."); return; }

            var blockProgProcessorType = game2Sim.GetType("Keen.Game2.Simulation.GameSystems.Progression.BlockProgressionCheckpointProcessor");
            if (blockProgProcessorType == null) { logger.LogWarning("Could not find BlockProgressionCheckpointProcessor type."); return; }

            var instanceProp = definitionManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                               ?? definitionManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);

            if (instanceProp == null) { logger.LogWarning("Could not find DefinitionManager.Instance property."); return; }

            var managerInstance = instanceProp.GetValue(null);
            if (managerInstance == null) { logger.LogWarning("DefinitionManager.Instance is null."); return; }

            // Need to find _postProcessorCache field
            var cacheField = definitionManagerType.GetField("_postProcessorCache", BindingFlags.NonPublic | BindingFlags.Instance);
            if (cacheField != null)
            {
                var cache = (IDictionary?)cacheField.GetValue(managerInstance);
                if (cache != null)
                {
                    if (!cache.Contains(blockProgProcessorType))
                    {
                        var processor = Activator.CreateInstance(blockProgProcessorType);
                        cache.Add(blockProgProcessorType, processor);
                        logger.LogInformation("Injected BlockProgressionCheckpointProcessor into DefinitionManager.");
                    }
                }
                else
                {
                    logger.LogWarning("_postProcessorCache is null.");
                }
            }
            else
            {
                logger.LogWarning("Could not find _postProcessorCache field.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing DefinitionManager.");
        }
    }

    private static void InitializeBumpAllocator(ILogger logger)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            if (vrageLibrary == null) return;

            var bumpAllocatorType = vrageLibrary.GetType("Keen.VRage.Library.Memory.BumpAllocator");
            if (bumpAllocatorType == null) return;

            var instanceField = bumpAllocatorType.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField == null) return;

            // Get the current value (should be default struct)
            var instance = instanceField.GetValue(null);

            var initMethod = bumpAllocatorType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Instance);
            if (initMethod != null)
            {
                initMethod.Invoke(instance, null);
                // Set the modified instance back to the static field
                instanceField.SetValue(null, instance);
                logger.LogInformation("BumpAllocator initialized.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error initializing BumpAllocator.");
        }
    }
}

