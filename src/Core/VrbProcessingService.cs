using Microsoft.Extensions.Logging;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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

    public void ProcessFile(string filePath, bool validate = false)
    {
        if (filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            RehydrateFile(filePath);
            return;
        }

        var fileName = Path.GetFileName(filePath);

        // Map filenames to expected types
        string? typeName = null;
        string? assemblyName = null;

        if (fileName.Equals("sessioncomponents", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("sessioncomponents.vrb", StringComparison.OrdinalIgnoreCase))
        {
            typeName = "Keen.Game2.Simulation.RuntimeSystems.Saves.SessionComponentsSnapshot";
            assemblyName = "Game2.Simulation";
        }
        else if (fileName.Equals("savegame", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("savegame.vrb", StringComparison.OrdinalIgnoreCase))
        {
            typeName = "Keen.VRage.Core.Game.Systems.EntityBundle";
            assemblyName = "VRage.Core.Game";
        }
        else if (fileName.Equals("assetjournal", StringComparison.OrdinalIgnoreCase) ||
                 fileName.Equals("assetjournal.vrb", StringComparison.OrdinalIgnoreCase))
        {
            typeName = "Keen.Game2.Game.EngineComponents.AssetJournal";
            assemblyName = "Game2.Game";
        }

        if (typeName != null && assemblyName != null)
        {
            Console.Error.WriteLine($"DEBUG: Dispatching to DeserializeFile for {fileName}");
            _logger.LogInformation("Processing file: {FileName} as {TypeName} (Validate: {Validate})", fileName, typeName, validate);
            DeserializeFile(filePath, typeName, assemblyName, validate);
        }
        else
        {
            Console.Error.WriteLine($"DEBUG: Unknown file type {fileName}");
            _logger.LogWarning("Unknown file type: {FileName}. Skipping.", fileName);
        }
    }

    private void RehydrateFile(string jsonPath)
    {
        try
        {
            _logger.LogInformation("Rehydrating JSON to VRB: {JsonPath}", jsonPath);
            
            // 1. Determine Output Path and Target Type from Filename
            // e.g. "savegame.vrb.json" -> "savegame.vrb" -> EntityBundle
            // e.g. "savegame.json" -> "savegame.vrb" -> EntityBundle
            
            var fileName = Path.GetFileName(jsonPath);
            string vrbName = fileName.EndsWith(".vrb.json", StringComparison.OrdinalIgnoreCase) 
                ? fileName.Replace(".vrb.json", ".vrb") 
                : fileName.Replace(".json", "");
                
            // If just "savegame", make it "savegame.vrb" if that's what we expect?
            // Actually the game uses extensionless files or .vrb sometimes. Let's assume .vrb for safety or match the deserializer.
            // But if the input was "savegame.vrb", we wrote "savegame.vrb.json" (maybe?)
            // Deserializer output was Console Write, so user piped it to something.
            // Let's assume the user knows what they are doing with filenames, but we need to identify the TYPE from the name still.
            
            string? typeName = null;
            string? assemblyName = null;
            
            // Check based on the *base* name (vrbName)
            if (vrbName.Contains("sessioncomponents", StringComparison.OrdinalIgnoreCase))
            {
                typeName = "Keen.Game2.Simulation.RuntimeSystems.Saves.SessionComponentsSnapshot";
                assemblyName = "Game2.Simulation";
            }
            else if (vrbName.Contains("savegame", StringComparison.OrdinalIgnoreCase))
            {
                typeName = "Keen.VRage.Core.Game.Systems.EntityBundle";
                assemblyName = "VRage.Core.Game";
            }
            else if (vrbName.Contains("assetjournal", StringComparison.OrdinalIgnoreCase))
            {
                typeName = "Keen.Game2.Game.EngineComponents.AssetJournal";
                assemblyName = "Game2.Game";
            }
            
            if (typeName == null || assemblyName == null)
            {
                 _logger.LogError("Could not determine target VRB type from filename {FileName}", fileName);
                 return;
            }

            // 2. Read and Parse JSON
            var jsonContent = File.ReadAllText(jsonPath);
            using var doc = JsonDocument.Parse(jsonContent);
            
            // 3. Rehydrate
            var obj = ObjectGraphHydrator.Hydrate(jsonContent);
            
            if (obj == null)
            {
                _logger.LogError("Failed to rehydrate object from JSON.");
                return;
            }
            
            // 4. Serialize to VRB
            var vrbPath = Path.Combine(Path.GetDirectoryName(jsonPath) ?? "", vrbName);
            
            // We need to call serialization. We can reuse VrbValidation logic by making it public or copying it.
            // But since VrbValidation is "Verify", let's implement Serialize here.
            
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            var targetAssembly = GameAssemblyManager.GetAssembly(assemblyName);
            
            if (vrageLibrary == null || targetAssembly == null) 
            {
                _logger.LogError("Missing assemblies.");
                return;
            }
            
            var targetType = targetAssembly.GetType(typeName);
            if (targetType == null)
            {
                _logger.LogError("Missing target type.");
                return;
            }

            // Using "None" compression as default for rehydrated files to ensure maximum compatibility/speed?
            // Or Brotli? Let's default to Brotli as it's common.
            SerializeObjectToFile(obj, vrbPath, targetType, vrageLibrary, "Brotli");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rehydrating {JsonPath}", jsonPath);
        }
    }

    private void SerializeObjectToFile(object data, string filePath, Type targetType, Assembly vrageLibrary, string compressionName)
    {
        try 
        {
            var writerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveWriter");
            var compressionTypeEnum = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.Archive.CompressionType");
            
            if (writerType == null || compressionTypeEnum == null) return;
            
            var writeMethod = writerType.GetMethod("AddMainChunk");
            if (writeMethod == null) return;
            
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
            
            var writer = Activator.CreateInstance(writerType, new object[] { context, false });
            
            genericWrite.Invoke(writer, new object[] { data, compressionVal });
            
            if (writer is IDisposable d) d.Dispose();
            
            _logger.LogInformation("Successfully rehydrated and saved to {VrbPath} (Compression: {Comp})", filePath, compressionVal);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, "Error writing VRB file {FilePath}", filePath);
        }
    }

    private void DeserializeFile(string filePath, string targetTypeName, string targetAssemblyName, bool validate)
    {
        try
        {
            var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library");
            var targetAssembly = GameAssemblyManager.GetAssembly(targetAssemblyName);

            if (vrageLibrary == null || targetAssembly == null)
            {
                _logger.LogError("Missing assemblies (Library: {LibraryFound}, Target: {TargetFound})", vrageLibrary != null, targetAssembly != null);
                return;
            }

            var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader");
            var targetType = targetAssembly.GetType(targetTypeName);

            if (readerType == null || targetType == null)
            {
                _logger.LogError("Could not find necessary types (Reader: {ReaderFound}, Target: {TargetFound} [{TargetTypeName}])", readerType != null, targetType != null, targetTypeName);
                return;
            }

            // --- DESERIALIZE ---
            object? result = null;
            using (var fs = File.OpenRead(filePath))
            {
                // Create Context
                var context = CreateSerializationContext(vrageLibrary, fs, Path.GetFileName(filePath));
                if (context == null) return;

                // Create Reader: new BinaryArchiveReader(SerializationContext context)
                var reader = Activator.CreateInstance(readerType, new object[] { context });

                // Call ReadMainChunk<T>()
                var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes)
                                 ?? readerType.BaseType?.GetMethod("ReadMainChunk", Type.EmptyTypes);

                if (readMethod != null && reader != null)
                {
                    var genericRead = readMethod.MakeGenericMethod(targetType);
                    result = genericRead.Invoke(reader, null);

                    _logger.LogInformation("Deserialization successful for {FilePath}", filePath);

                    // 1. Sanitize the Object Graph (Remove Span<T> and other unserializable types)
                    // This creates a safe Dictionary/List based representation of the data.
                    var sanitizedResult = DebugObjectConverter.ToDebugObject(result);

                    // 2. Standard JSON Serialization of the sanitized graph
                    // We use ReferenceHandler.IgnoreCycles because the Sanitizer maintains object identity for Dictionaries/Lists
                    // matching the original graph structure.
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        ReferenceHandler = ReferenceHandler.IgnoreCycles,
                        // IncludeFields is implied because Sanitizer explicitly extracted fields into the dictionary
                    };

                    var json = JsonSerializer.Serialize(sanitizedResult, options);

                    Console.WriteLine(json);
                    _logger.LogInformation("Success! Output JSON to console for {FilePath}", filePath);

                    // Validation (Round-Trip) if requested
                    if (validate && result != null)
                    {
                        // Perform FULL-CYCLE Validation
                        // 1. VRB -> Object (Done, 'result')
                        // 2. Object -> JSON (Done, 'json')
                        // 3. JSON -> Object (Rehydration)
                        // 4. Object -> VRB (Hash Check)
                        
                        _logger.LogInformation("Performing Full-Cycle Validation (VRB -> JSON -> VRB)...");
                        var rehydrated = ObjectGraphHydrator.Hydrate(json);
                        
                        if (rehydrated != null)
                        {
                            VrbValidation.VerifyRoundTrip(rehydrated, filePath, targetType, vrageLibrary, targetTypeName, targetAssemblyName, _logger);
                        }
                        else
                        {
                            _logger.LogError("Validation Failed: Could not rehydrate object from generated JSON.");
                        }
                    }
                }
                else
                {
                    _logger.LogError("Error: Could not find ReadMainChunk method.");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[CRITICAL ERROR] {ex}");
            _logger.LogError(ex, "Error deserializing {FilePath}", filePath);
        }
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
