using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Vrb.Infrastructure;

namespace Vrb.Utils;

/// <summary>
/// Helper class for creating VRage serialization contexts.
/// Centralizes the logic for setting up SerializationContext with required custom contexts.
/// </summary>
internal static class SerializationContextHelper
{
    /// <summary>
    /// Creates a SerializationContext for VRB (binary) serialization operations.
    /// Configures required custom contexts like EntityProxySerializationContext and DummyDefinitionSerializationContext.
    /// </summary>
    /// <param name="vrageLibrary">The loaded VRage.Library assembly.</param>
    /// <param name="stream">The stream to read from or write to.</param>
    /// <param name="debugName">A debug name for the context (typically the filename).</param>
    /// <returns>A configured SerializationContext instance.</returns>
    /// <exception cref="TypeLoadException">Thrown if required types cannot be found in the assemblies.</exception>
    /// <exception cref="InvalidOperationException">Thrown if context creation fails.</exception>
    public static object CreateBinaryContext(Assembly vrageLibrary, Stream stream, string debugName)
    {
        var vrageDcs = GameAssemblyManager.GetAssembly("VRage.DCS");

        var contextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("SerializationContext not found");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext")
            ?? throw new TypeLoadException("CustomSerializationContext not found");

        var customContexts = new List<object>();

        // Add EntityProxySerializationContext (required for Entity serialization)
        if (vrageDcs != null)
        {
            var proxyContextType = vrageDcs.GetType("Keen.VRage.DCS.Serialization.EntityProxySerializationContext");
            if (proxyContextType != null)
            {
                var instance = Activator.CreateInstance(proxyContextType);
                if (instance != null) customContexts.Add(instance);
            }
        }

        // Add a smart DefinitionSerializationContext that uses our loaded definitions
        // This allows proper type resolution for abstract definition types
        var logger = Vrb.Core.Vrb.LoggerFactory?.CreateLogger("SerializationContextHelper");
        var smartContext = SmartDefinitionSerializationContextFactory.Create(logger);
        if (smartContext != null)
        {
            customContexts.Add(smartContext);
        }
        else
        {
            // Fallback to DummyDefinitionSerializationContext if smart context creation fails
            var dummyDefContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
            if (dummyDefContextType != null)
            {
                var instance = Activator.CreateInstance(dummyDefContextType);
                if (instance != null) customContexts.Add(instance);
            }
        }

        // Build the typed array for the constructor
        var contextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
        {
            contextsArray.SetValue(customContexts[i], i);
        }

        return Activator.CreateInstance(contextType, new object[] { stream, debugName, contextsArray })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");
    }

    /// <summary>
    /// Creates a SerializationContext for JSON serialization operations.
    /// Includes JsonSerializationParameters in addition to standard custom contexts.
    /// </summary>
    /// <param name="vrageLibrary">The loaded VRage.Library assembly.</param>
    /// <param name="stream">The stream to read from or write to.</param>
    /// <param name="debugName">A debug name for the context.</param>
    /// <param name="useArchiveFormat">Whether to use archive format (includes $Type, $Bundles, etc.).</param>
    /// <returns>A configured SerializationContext instance for JSON operations.</returns>
    public static object CreateJsonContext(Assembly vrageLibrary, Stream stream, string debugName, bool useArchiveFormat = true)
    {
        var vrageDcs = GameAssemblyManager.GetAssembly("VRage.DCS");

        var contextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("SerializationContext not found");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext")
            ?? throw new TypeLoadException("CustomSerializationContext not found");
        var jsonParamsType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Json.JsonSerializationParameters")
            ?? throw new TypeLoadException("JsonSerializationParameters not found");

        var customContexts = new List<object>();

        // Add JsonSerializationParameters (controls archive format output)
        var jsonParams = Activator.CreateInstance(jsonParamsType, new object[] { useArchiveFormat });
        if (jsonParams != null) customContexts.Add(jsonParams);

        // Add EntityProxySerializationContext
        if (vrageDcs != null)
        {
            var proxyContextType = vrageDcs.GetType("Keen.VRage.DCS.Serialization.EntityProxySerializationContext");
            if (proxyContextType != null)
            {
                var instance = Activator.CreateInstance(proxyContextType);
                if (instance != null) customContexts.Add(instance);
            }
        }

        // Add a smart DefinitionSerializationContext that uses our loaded definitions
        var jsonLogger = Vrb.Core.Vrb.LoggerFactory?.CreateLogger("SerializationContextHelper");
        var smartContextForJson = SmartDefinitionSerializationContextFactory.Create(jsonLogger);
        if (smartContextForJson != null)
        {
            customContexts.Add(smartContextForJson);
        }
        else
        {
            // Fallback to DummyDefinitionSerializationContext
            var dummyDefContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
            if (dummyDefContextType != null)
            {
                var instance = Activator.CreateInstance(dummyDefContextType);
                if (instance != null) customContexts.Add(instance);
            }
        }

        // Build the typed array
        var contextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
        {
            contextsArray.SetValue(customContexts[i], i);
        }

        return Activator.CreateInstance(contextType, new object[] { stream, debugName, contextsArray })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");
    }
}

