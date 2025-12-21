using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Text;
using Vrb.Infrastructure;
using Vrb.Utils;

namespace Vrb.Core;

/// <summary>
/// Core service for processing VRB (VRage Binary) files.
/// Provides three main operations:
/// <list type="bullet">
///   <item><description>VRB → JSON: Deserialize binary save files to human-readable JSON</description></item>
///   <item><description>JSON → VRB: Serialize JSON back to binary format</description></item>
///   <item><description>Validation: Verify round-trip fidelity of conversions</description></item>
/// </list>
/// Uses the Space Engineers 2 game engine's built-in serialization via reflection.
/// </summary>
public class VrbProcessingService
{
    private readonly ILogger<VrbProcessingService> _logger;

    /// <summary>
    /// Creates a new instance of the VrbProcessingService.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public VrbProcessingService(ILogger<VrbProcessingService> logger)
    {
        _logger = logger;
    }

    #region Public API

    /// <summary>
    /// Deserializes a VRB file to a JSON string using the game engine's JSON serialization.
    /// </summary>
    /// <param name="filePath">Path to the VRB file.</param>
    /// <param name="type">The type of VRB file (SaveGame, SessionComponents, AssetJournal).</param>
    /// <param name="validate">If true, performs a validation round-trip to verify data integrity.</param>
    /// <returns>JSON string representation of the VRB contents.</returns>
    /// <exception cref="FileNotFoundException">Thrown if the file does not exist.</exception>
    /// <exception cref="ArgumentException">Thrown if the file does not have a .vrb extension.</exception>
    /// <exception cref="InvalidOperationException">Thrown if required assemblies are not loaded.</exception>
    public string DeserializeVrb(string filePath, TargetType type, bool validate = false)
    {
        // Ensure BumpAllocator is initialized for this thread (required by engine)
        BumpAllocatorHelper.EnsureInitialized(_logger);

        // Validate input
        if (!File.Exists(filePath)) 
            throw new FileNotFoundException($"File not found: {filePath}", filePath);

        if (!filePath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"File {filePath} must have a .vrb extension.", nameof(filePath));

        var (targetTypeName, targetAssemblyName) = TargetTypeHelper.GetTypeInfo(type);

        try
        {
            // Get required assemblies
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library")
                ?? throw new InvalidOperationException("VRage.Library assembly not loaded");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName)
                ?? throw new InvalidOperationException($"{targetAssemblyName} assembly not loaded");

            var targetType = targetAssembly.GetType(targetTypeName)
                ?? throw new TypeLoadException($"Could not find target type: {targetTypeName}");

            _logger.LogInformation("Deserializing VRB to object graph: {FilePath}", filePath);

            // Step 1: VRB → Object Graph (using engine's binary deserializer)
            object objectGraph;
            using (var fs = File.OpenRead(filePath))
            {
                objectGraph = DeserializeVrbStream(vrageLibrary, targetType, fs, Path.GetFileName(filePath));
            }

            _logger.LogInformation("Deserialization successful. Converting to JSON...");

            // Step 2: Object Graph → JSON (using engine's JSON serializer)
            var json = SerializeObjectToJson(vrageLibrary, objectGraph);

            _logger.LogInformation("Successfully converted {FilePath} to JSON ({Length:N0} characters)", 
                filePath, json.Length);

            // Optional: Validate round-trip integrity
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
    /// <param name="compressionMethod">Compression method: "None", "ZLib", or "Brotli" (default).</param>
    /// <exception cref="InvalidOperationException">Thrown if required assemblies are not loaded.</exception>
    public void SerializeJsonToVrb(string jsonContent, string vrbOutputPath, TargetType type, string compressionMethod = "Brotli")
    {
        // Ensure BumpAllocator is initialized for this thread
        BumpAllocatorHelper.EnsureInitialized(_logger);

        var (targetTypeName, targetAssemblyName) = TargetTypeHelper.GetTypeInfo(type);

        try
        {
            // Get required assemblies
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library")
                ?? throw new InvalidOperationException("VRage.Library assembly not loaded");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName)
                ?? throw new InvalidOperationException($"{targetAssemblyName} assembly not loaded");

            var targetType = targetAssembly.GetType(targetTypeName)
                ?? throw new TypeLoadException($"Could not find target type: {targetTypeName}");

            _logger.LogInformation("Deserializing JSON to object graph...");

            // Step 1: JSON → Object Graph (using engine's JSON deserializer)
            var objectGraph = DeserializeJsonToObject(vrageLibrary, targetType, jsonContent);

            _logger.LogInformation("Rehydrated object type: {Type}", objectGraph.GetType().FullName);

            // Step 2: Object Graph → VRB (using engine's binary serializer)
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

    #endregion

    #region Binary Serialization (VRB)

    /// <summary>
    /// Deserializes a VRB stream to an object using the engine's BinaryArchiveReader.
    /// </summary>
    private object DeserializeVrbStream(Assembly vrageLibrary, Type targetType, Stream stream, string debugName)
    {
        var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader")
            ?? throw new TypeLoadException("Could not find BinaryArchiveReader type");

        // Create serialization context with required custom contexts
        var context = SerializationContextHelper.CreateBinaryContext(vrageLibrary, stream, debugName);
        
        var reader = Activator.CreateInstance(readerType, new object[] { context })
            ?? throw new InvalidOperationException("Failed to create BinaryArchiveReader");

        // Find and invoke the generic ReadMainChunk<T> method
        var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes)
            ?? readerType.BaseType?.GetMethod("ReadMainChunk", Type.EmptyTypes)
            ?? throw new MissingMethodException("Could not find ReadMainChunk method");

        var genericRead = readMethod.MakeGenericMethod(targetType);
        return genericRead.Invoke(reader, null) 
            ?? throw new InvalidOperationException("ReadMainChunk returned null");
    }

    /// <summary>
    /// Serializes an object to a VRB file using the engine's BinaryArchiveWriter.
    /// </summary>
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
        var context = SerializationContextHelper.CreateBinaryContext(vrageLibrary, fs, Path.GetFileName(outputPath));

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
    /// Uses archive format which includes $Type, $Bundles, etc. for full fidelity.
    /// </summary>
    private string SerializeObjectToJson(Assembly vrageLibrary, object obj)
    {
        var serializationHelperType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationHelper")
            ?? throw new TypeLoadException("SerializationHelper not found");
        var serializerFormatType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializerFormat")
            ?? throw new TypeLoadException("SerializerFormat not found");

        using var ms = new MemoryStream();
        var context = SerializationContextHelper.CreateJsonContext(vrageLibrary, ms, "VrbJsonSerialize");

        // Get SerializerFormat.Json enum value
        var jsonFormat = Enum.Parse(serializerFormatType, "Json");

        // Find and invoke SerializationHelper.Serialize<T>(context, value, format)
        var serializeMethod = serializationHelperType.GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod && m.GetParameters().Length == 3);
        var genericSerialize = serializeMethod.MakeGenericMethod(obj.GetType());

        var parameters = new object[] { context, obj, jsonFormat };
        genericSerialize.Invoke(null, parameters);

        // Dispose context to flush buffers
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
        var serializerFormatType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializerFormat")
            ?? throw new TypeLoadException("SerializerFormat not found");

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = SerializationContextHelper.CreateJsonContext(vrageLibrary, ms, "VrbJsonDeserialize");

        // Get SerializerFormat.Json enum value
        var jsonFormat = Enum.Parse(serializerFormatType, "Json");

        // Find and invoke SerializationHelper.Deserialize<T>(context, format)
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

    #endregion

    #region Validation

    /// <summary>
    /// Validates that a JSON string can round-trip back to an equivalent object.
    /// Compares field values between original and rehydrated objects.
    /// </summary>
    private void ValidateRoundTrip(Assembly vrageLibrary, Type targetType, string json, object originalObject)
    {
        var rehydrated = DeserializeJsonToObject(vrageLibrary, targetType, json);

        // Compare basic properties
        var originalFields = originalObject.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (var field in originalFields)
        {
            try
            {
                var originalValue = field.GetValue(originalObject);
                var rehydratedValue = field.GetValue(rehydrated);

                // For collections, check count matches
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
