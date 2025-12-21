using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Text;
using Vrb.Infrastructure;
using Vrb.Utils;

namespace Vrb.Core;

/// <summary>
/// Service for processing VRB (VRage Binary) files.
/// Uses the game engine's built-in serialization for both Binary and JSON formats.
/// </summary>
public class VrbProcessingService
{
    private readonly ILogger<VrbProcessingService> _logger;

    public VrbProcessingService(ILogger<VrbProcessingService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Deserializes a VRB file to a JSON string using the game engine's JSON serialization.
    /// </summary>
    /// <param name="filePath">Path to the VRB file.</param>
    /// <param name="type">The type of VRB file (SaveGame, SessionComponents, AssetJournal).</param>
    /// <param name="validate">If true, performs a validation round-trip.</param>
    /// <returns>JSON string representation of the VRB contents.</returns>
    public string DeserializeVrb(string filePath, TargetType type, bool validate = false)
    {
        BumpAllocatorHelper.EnsureInitialized(_logger);

        if (!File.Exists(filePath)) 
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        if (!filePath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"File {filePath} must have a .vrb extension.", nameof(filePath));

        var (targetTypeName, targetAssemblyName) = GetTypeInfo(type);

        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library")
                ?? throw new InvalidOperationException("VRage.Library assembly not loaded");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName)
                ?? throw new InvalidOperationException($"{targetAssemblyName} assembly not loaded");

            var targetType = targetAssembly.GetType(targetTypeName)
                ?? throw new TypeLoadException($"Could not find target type: {targetTypeName}");

            _logger.LogInformation("Deserializing VRB to object graph: {FilePath}", filePath);

            // Step 1: VRB -> Object Graph (using engine's binary deserializer)
            object objectGraph;
            using (var fs = File.OpenRead(filePath))
            {
                objectGraph = DeserializeVrbStream(vrageLibrary, targetAssembly, targetType, fs, Path.GetFileName(filePath));
            }

            _logger.LogInformation("Deserialization successful. Converting to JSON...");

            // Step 2: Object Graph -> JSON (using engine's JSON serializer)
            var json = SerializeObjectToJson(vrageLibrary, objectGraph);

            _logger.LogInformation("Successfully converted {FilePath} to JSON ({Length:N0} characters)", 
                filePath, json.Length);

            if (validate)
            {
                _logger.LogInformation("Validating round-trip...");
                ValidateRoundTrip(vrageLibrary, targetType, json, objectGraph);
            }

            return json;
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error deserializing {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// Serializes JSON content to a VRB file using the game engine's serialization.
    /// </summary>
    /// <param name="jsonContent">JSON string to convert.</param>
    /// <param name="vrbOutputPath">Output path for the VRB file.</param>
    /// <param name="type">The type of VRB file.</param>
    /// <param name="compressionMethod">Compression method (None, ZLib, Brotli).</param>
    public void SerializeJsonToVrb(string jsonContent, string vrbOutputPath, TargetType type, string compressionMethod = "Brotli")
    {
        BumpAllocatorHelper.EnsureInitialized(_logger);

        var (targetTypeName, targetAssemblyName) = GetTypeInfo(type);

        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library")
                ?? throw new InvalidOperationException("VRage.Library assembly not loaded");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName)
                ?? throw new InvalidOperationException($"{targetAssemblyName} assembly not loaded");

            var targetType = targetAssembly.GetType(targetTypeName)
                ?? throw new TypeLoadException($"Could not find target type: {targetTypeName}");

            _logger.LogInformation("Deserializing JSON to object graph...");

            // Step 1: JSON -> Object Graph (using engine's JSON deserializer)
            var objectGraph = DeserializeJsonToObject(vrageLibrary, targetType, jsonContent);

            _logger.LogInformation("Rehydrated object type: {Type}", objectGraph.GetType().FullName);

            // Step 2: Object Graph -> VRB (using engine's binary serializer)
            _logger.LogInformation("Serializing to VRB: {Path}", vrbOutputPath);
            SerializeObjectToVrb(vrageLibrary, targetType, objectGraph, vrbOutputPath, compressionMethod);

            _logger.LogInformation("Successfully saved to {VrbPath} (Compression: {Comp})", 
                vrbOutputPath, compressionMethod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JSON content to VRB.");
            throw;
        }
    }

    #region Binary Serialization (VRB)

    private object DeserializeVrbStream(Assembly vrageLibrary, Assembly targetAssembly, Type targetType, Stream stream, string debugName)
    {
        var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader")
            ?? throw new TypeLoadException("Could not find BinaryArchiveReader type");

        var context = CreateSerializationContext(vrageLibrary, stream, debugName);
        
        var reader = Activator.CreateInstance(readerType, new object[] { context })
            ?? throw new InvalidOperationException("Failed to create BinaryArchiveReader");

        var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes)
            ?? readerType.BaseType?.GetMethod("ReadMainChunk", Type.EmptyTypes)
            ?? throw new MissingMethodException("Could not find ReadMainChunk method");

        var genericRead = readMethod.MakeGenericMethod(targetType);
        return genericRead.Invoke(reader, null) 
            ?? throw new InvalidOperationException("ReadMainChunk returned null");
    }

    private void SerializeObjectToVrb(Assembly vrageLibrary, Type targetType, object objectGraph, string outputPath, string compressionMethod)
    {
        var writerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveWriter")
            ?? throw new TypeLoadException("Could not find BinaryArchiveWriter type");
        var compressionTypeEnum = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.Archive.CompressionType")
            ?? throw new TypeLoadException("Could not find CompressionType enum");

        var writeMethod = writerType.GetMethod("AddMainChunk")
            ?? throw new MissingMethodException("Could not find AddMainChunk method");

        var genericWrite = writeMethod.MakeGenericMethod(targetType);
        var compressionVal = Enum.Parse(compressionTypeEnum, compressionMethod);

        using var fs = File.Create(outputPath);
        var context = CreateSerializationContext(vrageLibrary, fs, Path.GetFileName(outputPath));

        var writer = Activator.CreateInstance(writerType, new object[] { context, false })
            ?? throw new InvalidOperationException("Failed to create BinaryArchiveWriter");

        try
        {
            genericWrite.Invoke(writer, new object[] { objectGraph, compressionVal });
        }
        finally
        {
            if (writer is IDisposable disposable)
                disposable.Dispose();
        }
    }

    #endregion

    #region JSON Serialization (Engine's built-in)

    /// <summary>
    /// Serializes an object to JSON using the game engine's JSON serialization.
    /// Uses archive format which includes $Type, $Bundles, etc.
    /// </summary>
    private string SerializeObjectToJson(Assembly vrageLibrary, object obj)
    {
        var serializationHelperType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationHelper")
            ?? throw new TypeLoadException("SerializationHelper not found");
        var serializationContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("SerializationContext not found");
        var jsonParamsType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Json.JsonSerializationParameters")
            ?? throw new TypeLoadException("JsonSerializationParameters not found");
        var serializerFormatType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializerFormat")
            ?? throw new TypeLoadException("SerializerFormat not found");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext")
            ?? throw new TypeLoadException("CustomSerializationContext not found");

        // Build custom contexts array
        var customContexts = BuildCustomContextsForJson(vrageLibrary, jsonParamsType);
        var customContextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
            customContextsArray.SetValue(customContexts[i], i);

        using var ms = new MemoryStream();
        var context = Activator.CreateInstance(serializationContextType, new object[] { ms, "VrbJsonSerialize", customContextsArray })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");

        // Get SerializerFormat.Json enum value
        var jsonFormat = Enum.Parse(serializerFormatType, "Json");

        // Call SerializationHelper.Serialize<T>(context, value, format)
        var serializeMethod = serializationHelperType.GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod && m.GetParameters().Length == 3);
        var genericSerialize = serializeMethod.MakeGenericMethod(obj.GetType());

        var parameters = new object[] { context, obj, jsonFormat };
        genericSerialize.Invoke(null, parameters);

        // Dispose context to flush
        if (context is IDisposable d) d.Dispose();

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Deserializes JSON to an object using the game engine's JSON deserialization.
    /// </summary>
    private object DeserializeJsonToObject(Assembly vrageLibrary, Type targetType, string json)
    {
        var serializationHelperType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationHelper")
            ?? throw new TypeLoadException("SerializationHelper not found");
        var serializationContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("SerializationContext not found");
        var jsonParamsType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Json.JsonSerializationParameters")
            ?? throw new TypeLoadException("JsonSerializationParameters not found");
        var serializerFormatType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializerFormat")
            ?? throw new TypeLoadException("SerializerFormat not found");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext")
            ?? throw new TypeLoadException("CustomSerializationContext not found");

        // Build custom contexts array
        var customContexts = BuildCustomContextsForJson(vrageLibrary, jsonParamsType);
        var customContextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
            customContextsArray.SetValue(customContexts[i], i);

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = Activator.CreateInstance(serializationContextType, new object[] { ms, "VrbJsonDeserialize", customContextsArray })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");

        // Get SerializerFormat.Json enum value
        var jsonFormat = Enum.Parse(serializerFormatType, "Json");

        // Call SerializationHelper.Deserialize<T>(context, format)
        var deserializeMethod = serializationHelperType.GetMethods()
            .First(m => m.Name == "Deserialize" && m.IsGenericMethod && m.GetParameters().Length == 2 
                && m.GetParameters()[1].ParameterType == serializerFormatType);
        var genericDeserialize = deserializeMethod.MakeGenericMethod(targetType);

        var result = genericDeserialize.Invoke(null, new[] { context, jsonFormat })
            ?? throw new InvalidOperationException("Deserialize returned null");

        // Dispose context
        if (context is IDisposable d) d.Dispose();

        return result;
    }

    /// <summary>
    /// Builds the list of custom contexts required for JSON serialization.
    /// </summary>
    private List<object> BuildCustomContextsForJson(Assembly vrageLibrary, Type jsonParamsType)
    {
        var vrageDcs = GameAssemblyManager.GetAssembly("VRage.DCS");
        var customContexts = new List<object>();

        // Add JsonSerializationParameters(useArchiveFormat: true)
        var jsonParams = Activator.CreateInstance(jsonParamsType, new object[] { true });
        if (jsonParams != null) customContexts.Add(jsonParams);

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

        // Add DummyDefinitionSerializationContext
        var dummyDefContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
        if (dummyDefContextType != null)
        {
            var instance = Activator.CreateInstance(dummyDefContextType);
            if (instance != null) customContexts.Add(instance);
        }

        return customContexts;
    }

    #endregion

    #region Helpers

    private static (string TypeName, string AssemblyName) GetTypeInfo(TargetType type)
    {
        return type switch
        {
            TargetType.SaveGame => ("Keen.VRage.Core.Game.Systems.EntityBundle", "VRage.Core.Game"),
            TargetType.SessionComponents => ("Keen.Game2.Simulation.RuntimeSystems.Saves.SessionComponentsSnapshot", "Game2.Simulation"),
            TargetType.AssetJournal => ("Keen.Game2.Game.EngineComponents.AssetJournal", "Game2.Game"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown target type")
        };
    }

    /// <summary>
    /// Creates a SerializationContext for binary (VRB) serialization.
    /// </summary>
    private object CreateSerializationContext(Assembly vrageLibrary, Stream stream, string debugName)
    {
        var vrageDcs = GameAssemblyManager.GetAssembly("VRage.DCS");

        var contextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("SerializationContext not found");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext")
            ?? throw new TypeLoadException("CustomSerializationContext not found");

        var customContexts = new List<object>();

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

        // Add DummyDefinitionSerializationContext
        var dummyDefContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
        if (dummyDefContextType != null)
        {
            var instance = Activator.CreateInstance(dummyDefContextType);
            if (instance != null) customContexts.Add(instance);
        }

        var contextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
            contextsArray.SetValue(customContexts[i], i);

        return Activator.CreateInstance(contextType, new object[] { stream, debugName, contextsArray })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");
    }

    /// <summary>
    /// Validates that a JSON string can round-trip back to an equivalent object.
    /// </summary>
    private void ValidateRoundTrip(Assembly vrageLibrary, Type targetType, string json, object originalObject)
    {
        var rehydrated = DeserializeJsonToObject(vrageLibrary, targetType, json);

        // Compare basic properties
        var originalFields = originalObject.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var rehydratedFields = rehydrated.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in originalFields)
        {
            try
            {
                var originalValue = field.GetValue(originalObject);
                var rehydratedValue = field.GetValue(rehydrated);

                if (originalValue is System.Collections.ICollection origCol && 
                    rehydratedValue is System.Collections.ICollection rehydCol)
                {
                    if (origCol.Count != rehydCol.Count)
                    {
                        _logger.LogWarning("Field {Field} count mismatch: {Original} vs {Rehydrated}", 
                            field.Name, origCol.Count, rehydCol.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Could not compare field {Field}: {Error}", field.Name, ex.Message);
            }
        }

        _logger.LogInformation("Round-trip validation completed.");
    }

    #endregion
}
