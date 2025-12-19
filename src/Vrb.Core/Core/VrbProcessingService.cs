using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using vrb.Infrastructure;
using vrb.Utils;

namespace vrb.Core;

public class VrbProcessingService
{
    private readonly ILogger<VrbProcessingService> _logger;

    public VrbProcessingService(ILogger<VrbProcessingService> logger)
    {
        _logger = logger;
    }

    public string DeserializeVrb(string filePath, TargetType type, bool validate = false)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException($"File not found: {filePath}", filePath);

        if (!filePath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"File {filePath} must have a .vrb extension.", nameof(filePath));
        }

        var (targetTypeName, targetAssemblyName) = GetTypeInfo(type);

        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName);

            if (vrageLibrary == null || targetAssembly == null)
            {
                var msg = $"Missing assemblies (Library: {vrageLibrary != null}, Target: {targetAssembly != null})";
                _logger.LogError(msg);
                throw new InvalidOperationException(msg);
            }

            var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader");
            var targetType = targetAssembly.GetType(targetTypeName);

            if (readerType == null || targetType == null)
            {
                var msg = $"Could not find necessary types (Reader: {readerType != null}, Target: {targetType != null} [{targetTypeName}])";
                _logger.LogError(msg);
                throw new TypeLoadException(msg);
            }

            object? result = null;
            using (var fs = File.OpenRead(filePath))
            {
                var context = CreateSerializationContext(vrageLibrary, fs, Path.GetFileName(filePath));
                if (context == null) throw new InvalidOperationException("Failed to create SerializationContext.");

                var reader = Activator.CreateInstance(readerType, new object[] { context });
                var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes)
                                 ?? readerType.BaseType?.GetMethod("ReadMainChunk", Type.EmptyTypes);

                if (readMethod != null && reader != null)
                {
                    var genericRead = readMethod.MakeGenericMethod(targetType);
                    result = genericRead.Invoke(reader, null);

                    _logger.LogInformation("Deserialization successful for {FilePath}", filePath);

                    var sanitizedResult = DebugObjectConverter.ToDebugObject(result);

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        ReferenceHandler = ReferenceHandler.IgnoreCycles,
                    };

                    var json = JsonSerializer.Serialize(sanitizedResult, options);

                    _logger.LogInformation("Success! Deserialized {FilePath} to JSON string.", filePath);

                    if (validate && result != null)
                    {
                        _logger.LogInformation("Performing Full-Cycle Validation (VRB -> JSON -> VRB)...");
                        var rehydrated = ObjectGraphHydrator.Hydrate(json);
                        
                        if (rehydrated != null)
                        {
                            VrbValidation.VerifyRoundTrip(rehydrated, filePath, targetType, vrageLibrary, targetTypeName, targetAssemblyName, _logger);
                        }
                        else
                        {
                            var msg = "Validation Failed: Could not rehydrate object from generated JSON.";
                            _logger.LogError(msg);
                            throw new InvalidOperationException(msg);
                        }
                    }

                    return json;
                }
                else
                {
                    _logger.LogError("Error: Could not find ReadMainChunk method.");
                    throw new MissingMethodException("Could not find ReadMainChunk method.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Error deserializing {FilePath}", filePath);
            throw;
        }
    }

    public void SerializeJsonToVrb(string jsonContent, string vrbOutputPath, TargetType type, string compressionMethod = "Brotli")
    {
        try
        {
            _logger.LogInformation("Rehydrating JSON to VRB...");
            
            // Rehydrate
            var obj = ObjectGraphHydrator.Hydrate(jsonContent);
            
            if (obj == null)
            {
                throw new InvalidOperationException("Failed to rehydrate object from JSON.");
            }
            
            var (targetTypeName, targetAssemblyName) = GetTypeInfo(type);

            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName);
            
            if (vrageLibrary == null || targetAssembly == null) 
            {
                throw new InvalidOperationException("Missing assemblies.");
            }
            
            var targetType = targetAssembly.GetType(targetTypeName);
            if (targetType == null)
            {
                throw new TypeLoadException($"Missing target type: {targetTypeName}");
            }

            SerializeObjectToFile(obj, vrbOutputPath, targetType, vrageLibrary, compressionMethod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JSON content to VRB.");
            throw;
        }
    }

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

    private void SerializeObjectToFile(object data, string filePath, Type targetType, Assembly vrageLibrary, string compressionName)
    {
        var writerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveWriter");
        var compressionTypeEnum = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.Archive.CompressionType");
        
        if (writerType == null || compressionTypeEnum == null) throw new TypeLoadException("Could not find BinaryArchiveWriter or CompressionType.");
        
        var writeMethod = writerType.GetMethod("AddMainChunk");
        if (writeMethod == null) throw new MissingMethodException("Could not find AddMainChunk method.");
        
        var genericWrite = writeMethod.MakeGenericMethod(targetType);
        
        var compressionVal = Enum.ToObject(compressionTypeEnum, 0); // Default None
        try 
        { 
             var parsed = Enum.Parse(compressionTypeEnum, compressionName); 
             if (parsed != null) compressionVal = parsed;
        } 
        catch {}
        
        using var fs = File.Create(filePath);
        var context = CreateSerializationContext(vrageLibrary, fs, Path.GetFileName(filePath));
        if (context == null)
        {
             throw new InvalidOperationException("Failed to create serialization context.");
        }
        
        var writer = Activator.CreateInstance(writerType, new object[] { context, false });
        
        genericWrite.Invoke(writer, new object[] { data, compressionVal });
        
        if (writer is IDisposable d) d.Dispose();
        
        _logger.LogInformation("Successfully saved to {VrbPath} (Compression: {Comp})", filePath, compressionVal);
    }

    private object? CreateSerializationContext(Assembly vrageLibrary, Stream stream, string debugName)
    {
        var vrageDcs = GameAssemblyManager.GetAssembly("VRage.DCS");

        var contextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext");
        var customContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.CustomSerializationContext");

        if (contextType == null || customContextType == null)
        {
            _logger.LogError("Error: Could not find SerializationContext types.");
            return null;
        }

        var customContexts = new List<object>();
        if (vrageDcs != null)
        {
            var proxyContextType = vrageDcs.GetType("Keen.VRage.DCS.Serialization.EntityProxySerializationContext");
            if (proxyContextType != null)
            {
                var instance = Activator.CreateInstance(proxyContextType);
                if (instance != null)
                {
                    customContexts.Add(instance);
                }
            }
        }

        var dummyDefinitionContextType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
        if (dummyDefinitionContextType != null)
        {
            var instance = Activator.CreateInstance(dummyDefinitionContextType);
            if (instance != null)
            {
                customContexts.Add(instance);
            }
        }

        var contextsArray = Array.CreateInstance(customContextType, customContexts.Count);
        for (var i = 0; i < customContexts.Count; i++)
        {
            contextsArray.SetValue(customContexts[i], i);
        }

        return Activator.CreateInstance(contextType, new object[] { stream, debugName, contextsArray });
    }
}