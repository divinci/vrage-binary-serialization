using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Vrb.Infrastructure;
using Vrb.Utils;

namespace Vrb.Core.Utils;

/// <summary>
/// Helper class for loading and querying game definitions.
/// Loads definition metadata from the game's definitionsets.vrb files to enable
/// proper type resolution during deserialization of save files.
/// </summary>
public class DefinitionsHelper
{
    private static DefinitionsHelper? _instance;
    private static readonly object _lock = new();

    private readonly Dictionary<Guid, Type> _definitionTypes = new();
    private readonly ILogger? _logger;

    /// <summary>
    /// Gets the singleton instance of DefinitionsHelper.
    /// </summary>
    public static DefinitionsHelper Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new DefinitionsHelper();
                }
            }
            return _instance;
        }
    }

    /// <summary>
    /// Whether definitions have been successfully loaded.
    /// </summary>
    public bool IsLoaded => _definitionTypes.Count > 0;

    /// <summary>
    /// The number of definitions loaded.
    /// </summary>
    public int DefinitionCount => _definitionTypes.Count;

    private DefinitionsHelper()
    {
        _logger = Vrb.LoggerFactory?.CreateLogger<DefinitionsHelper>();
        LoadDefinitions();
    }

    /// <summary>
    /// Tries to get the concrete type for a definition GUID.
    /// </summary>
    /// <param name="guid">The definition GUID.</param>
    /// <param name="type">The concrete type if found.</param>
    /// <returns>True if the type was found, false otherwise.</returns>
    public bool TryGetDefinitionType(Guid guid, [MaybeNullWhen(false)] out Type type)
    {
        return _definitionTypes.TryGetValue(guid, out type);
    }

    /// <summary>
    /// Gets all loaded definitions.
    /// </summary>
    /// <returns>A dictionary of GUID to Type mappings.</returns>
    public IReadOnlyDictionary<Guid, Type> GetAllDefinitions()
    {
        return _definitionTypes;
    }

    /// <summary>
    /// Finds all definitions whose type name contains the specified search term.
    /// </summary>
    /// <param name="searchTerm">The term to search for in type names.</param>
    /// <returns>A dictionary of matching definitions.</returns>
    public Dictionary<Guid, Type> FindDefinitionsByTypeName(string searchTerm)
    {
        return _definitionTypes
            .Where(kvp => kvp.Value.FullName?.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ?? false)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Known locations of definitionsets.vrb files relative to the game install path.
    /// </summary>
    private static readonly string[] DefinitionSetsPaths =
    [
        @"GameData\Vanilla\Content\definitionsets.vrb",
        @"VRage\GameData\Engine\Content\definitionsets.vrb"
    ];

    private void LoadDefinitions()
    {
        try
        {
            var gamePath = GameInstallLocator.CachedGamePath;
            if (string.IsNullOrEmpty(gamePath))
            {
                _logger?.LogWarning("Game path not available. Cannot load definitions.");
                return;
            }

            // Find definitionsets.vrb files in known locations
            var definitionSetsFiles = DefinitionSetsPaths
                .Select(relativePath => Path.Combine(gamePath, relativePath))
                .Where(File.Exists)
                .ToList();
            
            if (definitionSetsFiles.Count == 0)
            {
                _logger?.LogWarning("No definitionsets.vrb files found in known locations under: {GamePath}", gamePath);
                return;
            }

            _logger?.LogInformation("Found {Count} definitionsets.vrb files", definitionSetsFiles.Count);

            foreach (var defSetsFile in definitionSetsFiles)
            {
                _logger?.LogInformation("Loading definitions from: {File}", defSetsFile);
                try
                {
                    LoadDefinitionsFromFile(defSetsFile);
                    _logger?.LogInformation("Successfully processed: {File}", defSetsFile);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load definitions from: {File}", defSetsFile);
                }
            }

            _logger?.LogInformation("Loaded {Count} total definitions", _definitionTypes.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error loading definitions");
        }
    }

    private void LoadDefinitionsFromFile(string filePath)
    {
        // Ensure BumpAllocator is initialized (required by the engine's serialization)
        BumpAllocatorHelper.EnsureInitialized(_logger);

        var vrageLibrary = GameAssemblyManager.GetAssembly("VRage.Library")
            ?? throw new InvalidOperationException("VRage.Library not loaded");

        // Get the types we need for deserialization
        var definitionSetCollectionType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionSetCollection")
            ?? throw new TypeLoadException("Could not find DefinitionSetCollection type");
        
        var definitionListType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionList")
            ?? throw new TypeLoadException("Could not find DefinitionList type");

        var definitionLoadingDataType = vrageLibrary.GetType("Keen.VRage.Library.Definitions.DefinitionLoadingData")
            ?? throw new TypeLoadException("Could not find DefinitionLoadingData type");

        // Use BinaryArchiveReader just like VrbProcessingService does
        var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader")
            ?? throw new TypeLoadException("Could not find BinaryArchiveReader type");

        using var fileStream = File.OpenRead(filePath);
        
        // Create a simple SerializationContext (no custom contexts needed for definition sets)
        var serializationContextType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.SerializationContext")
            ?? throw new TypeLoadException("Could not find SerializationContext type");

        var context = Activator.CreateInstance(serializationContextType, new object[] { fileStream, Path.GetFileName(filePath) })
            ?? throw new InvalidOperationException("Failed to create SerializationContext");

        // Create BinaryArchiveReader
        var reader = Activator.CreateInstance(readerType, new object[] { context })
            ?? throw new InvalidOperationException("Failed to create BinaryArchiveReader");

        try
        {
            // Find and invoke ReadMainChunk<DefinitionSetCollection>()
            var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes)
                ?? readerType.BaseType?.GetMethod("ReadMainChunk", Type.EmptyTypes)
                ?? throw new MissingMethodException("Could not find ReadMainChunk method");

            _logger?.LogDebug("Found ReadMainChunk method, invoking for type: {Type}", definitionSetCollectionType.FullName);
            
            var genericRead = readMethod.MakeGenericMethod(definitionSetCollectionType);
            var definitionSetCollection = genericRead.Invoke(reader, null);

            if (definitionSetCollection == null)
            {
                _logger?.LogWarning("Failed to deserialize DefinitionSetCollection from: {File}", filePath);
                return;
            }
            
            _logger?.LogDebug("Successfully deserialized DefinitionSetCollection");

            // Extract the DefinitionSets dictionary
            var definitionSetsProp = definitionSetCollectionType.GetProperty("DefinitionSets");
            if (definitionSetsProp == null)
            {
                _logger?.LogWarning("Could not find DefinitionSets property");
                return;
            }

            var definitionSetsRaw = definitionSetsProp.GetValue(definitionSetCollection);
            _logger?.LogInformation("DefinitionSets property value type: {Type}, IsNull: {IsNull}", 
                definitionSetsRaw?.GetType().FullName ?? "null", definitionSetsRaw == null);
            
            var definitionSets = definitionSetsRaw as System.Collections.IDictionary;
            if (definitionSets == null)
            {
                _logger?.LogWarning("DefinitionSets is null or not a dictionary");
                return;
            }
            
            _logger?.LogInformation("DefinitionSets count: {Count}", definitionSets.Count);

            // Process each definition set
            foreach (System.Collections.DictionaryEntry setEntry in definitionSets)
            {
                var setName = setEntry.Key as string;
                var definitionList = setEntry.Value;

                if (definitionList == null) continue;

                // Get the Definitions property from the DefinitionList
                // Note: Definitions returns DictionaryReader<Guid, DefinitionLoadingData> which is NOT IDictionary
                // We need to use reflection to access its Keys property and indexer
                var definitionsProp = definitionListType.GetProperty("Definitions");
                if (definitionsProp == null)
                {
                    _logger?.LogWarning("Could not find Definitions property on DefinitionList");
                    continue;
                }

                var definitionsReader = definitionsProp.GetValue(definitionList);
                if (definitionsReader == null)
                {
                    _logger?.LogWarning("Definitions is null for set '{SetName}'", setName);
                    continue;
                }

                var dictReaderType = definitionsReader.GetType();
                
                // Get the Keys property (returns HashSetReader<Guid>)
                var keysProp = dictReaderType.GetProperty("Keys");
                if (keysProp == null)
                {
                    _logger?.LogWarning("Could not find Keys property on DictionaryReader. Type: {Type}", dictReaderType.FullName);
                    continue;
                }

                var keysReader = keysProp.GetValue(definitionsReader);
                if (keysReader == null)
                {
                    _logger?.LogWarning("Keys is null for set '{SetName}'", setName);
                    continue;
                }

                // HashSetReader should implement IEnumerable<Guid>
                if (keysReader is not IEnumerable<Guid> keys)
                {
                    // Try to cast to IEnumerable
                    if (keysReader is System.Collections.IEnumerable enumerableKeys)
                    {
                        keys = enumerableKeys.Cast<Guid>();
                    }
                    else
                    {
                        _logger?.LogWarning("Keys is not IEnumerable for set '{SetName}'. Type: {Type}", 
                            setName, keysReader.GetType().FullName);
                        continue;
                    }
                }

                // Get the indexer for accessing definitions by Guid
                var indexerMethod = dictReaderType.GetMethod("get_Item") 
                    ?? dictReaderType.GetProperty("Item")?.GetGetMethod();
                
                // Get the Type property from DefinitionLoadingData
                var typeProp = definitionLoadingDataType.GetProperty("Type");
                if (typeProp == null)
                {
                    _logger?.LogWarning("Could not find Type property on DefinitionLoadingData");
                    continue;
                }

                int count = 0;
                foreach (var guid in keys)
                {
                    try
                    {
                        // Get the DefinitionLoadingData for this Guid
                        object? loadingData = null;
                        if (indexerMethod != null)
                        {
                            loadingData = indexerMethod.Invoke(definitionsReader, new object[] { guid });
                        }
                        
                        if (loadingData == null) continue;

                        var defType = typeProp.GetValue(loadingData) as Type;
                        if (defType != null && !_definitionTypes.ContainsKey(guid))
                        {
                            _definitionTypes[guid] = defType;
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "Failed to extract definition for Guid {Guid}", guid);
                    }
                }

                _logger?.LogDebug("Loaded {Count} definitions from set '{SetName}'", count, setName);
            }
        }
        finally
        {
            if (reader is IDisposable disposable)
                disposable.Dispose();
        }
    }
}

