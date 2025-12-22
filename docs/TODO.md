# TODO: Proper Definition Loading for SessionComponents Support

---

## ✅ IMPLEMENTATION COMPLETE (Updated: 2025-12-21)

### All Tests Passing: 30/30

The implementation is **complete**. All tests pass, including the previously-failing SessionComponents tests.

### Approach: Test-Driven Development (TDD)

We used a TDD approach:
1. ✅ **DefinitionHelper Tests First** - Created tests that define expected behavior
2. ✅ **DefinitionsHelper Implementation** - Built the helper to make tests pass
3. ✅ **SessionComponents Integration** - Replaced `DummyDefinitionSerializationContext` with smart context

### Files Created

| File | Purpose | Status |
|------|---------|--------|
| `tests/Vrb.Tests/DefinitionHelperTests.cs` | TDD tests for definition loading | ✅ Complete |
| `src/Vrb.Core/Utils/DefinitionsHelper.cs` | Singleton to load/query game definitions | ✅ Complete |
| `src/Vrb.Core/Utils/SmartDefinitionSerializationContext.cs` | Dynamic context using IL generation | ✅ Complete |

### Key Decisions Made

#### 1. Definition Set Locations
The user confirmed the exact locations of `definitionsets.vrb` files:
```
SpaceEngineers2\GameData\Vanilla\Content\definitionsets.vrb
SpaceEngineers2\VRage\GameData\Engine\Content\definitionsets.vrb
```
These are now hardcoded in `DefinitionsHelper.DefinitionSetsPaths`.

#### 2. Use `BinaryArchiveReader.ReadMainChunk<T>()` Pattern
After reviewing `VrbProcessingService.DeserializeVrbStream`, we use the same pattern:
```csharp
var readerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader");
var reader = Activator.CreateInstance(readerType, new object[] { context });
var readMethod = readerType.GetMethod("ReadMainChunk", Type.EmptyTypes);
var genericRead = readMethod.MakeGenericMethod(definitionSetCollectionType);
var result = genericRead.Invoke(reader, null);
```
This is simpler than manually parsing with `BinaryArchiveParser`.

#### 3. Cached Game Path
Added `GameInstallLocator.CachedGamePath` static property so `DefinitionsHelper` can access the game path after initialization.

#### 4. Logger Factory Exposure
Added `Vrb.LoggerFactory` property to enable logging in the singleton `DefinitionsHelper`.

#### 5. DictionaryReader Keys Iteration (SOLVED)
`DictionaryReader` has a `Keys` property returning `HashSetReader<Guid>` which implements `IEnumerable<Guid>`.
We use reflection to get the Keys, then iterate and use the indexer to get each `DefinitionLoadingData`.

#### 6. Dynamic Subclass Generation with IL.Emit
To replace `DummyDefinitionSerializationContext`, we use `System.Reflection.Emit` to dynamically generate 
a subclass of `DefinitionSerializationContext` that overrides `TryLocateDefinition`.

#### 7. Reflection for Internal Methods
`DefinitionHelper.CreatePlaceholder` is internal, so we call it via reflection from `DefinitionLookupHelper.CreateSmartPlaceholder`.

---

## 🔥 PROBLEM SOLVED: Abstract Type Instantiation

### The Original Problem

`DummyDefinitionSerializationContext` was calling `DefinitionHelper.CreatePlaceholder(hintType, id)` which 
used `FastActivator` to create an instance. When `hintType` was abstract (e.g., `ProgressionCheckpointDefinition`),
this failed with:

```
System.InvalidOperationException: Can't compile a NewExpression with a constructor declared on an abstract class
```

### The Solution

1. **Load 11,428 definitions** from the game's `definitionsets.vrb` files into `DefinitionsHelper`
2. **Create a smart context** that:
   - Looks up the GUID in `DefinitionsHelper` to get the **concrete type**
   - Calls `CreatePlaceholder` with the concrete type (e.g., `ProductionBlockProgressionCheckpointDefinition`)
   - Falls back to `hintType` if it's not abstract

### Result

All 30 tests now pass, including:
- `VrbToJson_SessionComponents_ReturnsValidJson` ✅
- `RoundTrip_SessionComponents_Succeeds` ✅
- `RoundTrip_AllSaveGames_Succeed` ✅

---

## 📚 HISTORICAL NOTES (Preserved for Reference)

### Previous DictionaryReader Challenge (Solved)

**Option A:** Use reflection to call its enumerator:
```csharp
// Check if it implements IEnumerable and iterate via reflection
var getEnumeratorMethod = definitionsRaw.GetType().GetMethod("GetEnumerator");
var enumerator = getEnumeratorMethod.Invoke(definitionsRaw, null);
// ... iterate using MoveNext/Current pattern
```

**Option B:** Cast to `IEnumerable<KeyValuePair<Guid, DefinitionLoadingData>>` (may work if it's covariant)

**Option C:** Look for an indexer or `Keys`/`Values` properties on `DictionaryReader`

**Recommended:** Examine the decompiled `DictionaryReader.cs` to find its exact interface.

---

## 📋 NEXT STEPS FOR CONTINUING ENGINEER

### Immediate Task: Fix `DictionaryReader` Iteration

1. **Find `DictionaryReader.cs`** in decompiled source:
   - Search in `decompiled/VRage.Library/Keen.VRage.Library.Collections.Readers/`
   - Identify what interfaces it implements

2. **Update `DefinitionsHelper.LoadDefinitionsFromFile`**:
   - Replace the `IDictionary` cast with proper iteration
   - Example fix if it implements `IEnumerable`:
   ```csharp
   // Instead of:
   var definitions = definitionsRaw as System.Collections.IDictionary;
   
   // Use reflection to iterate:
   foreach (var kvp in (IEnumerable)definitionsRaw)
   {
       var keyProp = kvp.GetType().GetProperty("Key");
       var valueProp = kvp.GetType().GetProperty("Value");
       var guid = (Guid)keyProp.GetValue(kvp);
       var loadingData = valueProp.GetValue(kvp);
       // ... extract Type from loadingData
   }
   ```

3. **Run tests to verify**:
   ```powershell
   dotnet test --filter "FullyQualifiedName~DefinitionHelperTests"
   ```

### After DefinitionsHelper Works

1. **Create `VrbDefinitionSerializationContext`** (Phase 2 in original plan)
   - Custom context that uses `DefinitionsHelper.TryGetDefinitionType(guid)`
   - Returns concrete type instead of trying to instantiate abstract `hintType`

2. **Update `SerializationContextHelper`** to use new context instead of `DummyDefinitionSerializationContext`

3. **Run full test suite** including SessionComponents tests

---

## 📊 Current Test Status

| Test | Status | Notes |
|------|--------|-------|
| `DefinitionHelperTests.CanLoadDefinitionsFromGame` | ❌ Failing | DictionaryReader iteration broken |
| `DefinitionHelperTests.CanFindProgressionRelatedDefinitions` | ❌ Failing | Depends on above |
| `DefinitionHelperTests.TryGetDefinitionType_ReturnsConcreteType` | ❌ Failing | Depends on above |
| `DefinitionHelperTests.TryGetDefinitionType_ReturnsFalseForUnknownGuid` | ✅ Passing | Works correctly |
| `LibraryTests.VrbToJson_SaveGame_ReturnsValidJson` | ✅ Passing | Unaffected |
| `LibraryTests.VrbToJson_SessionComponents_ReturnsValidJson` | ❌ Failing | Original problem |

---

## Problem Summary

The Vrb.Core tool fails to deserialize `sessioncomponents.vrb` files because it uses `DummyDefinitionSerializationContext` which cannot handle abstract definition types.

### Root Cause Chain

1. `sessioncomponents.vrb` contains `PlayerProgressionData` which references `ProgressionCheckpointDefinition` (an abstract class)
2. When deserializing, the engine calls `Definition.LocateValue(context, guid, typeHint)` to resolve definition references
3. Vrb.Core provides `DummyDefinitionSerializationContext` which naively tries to instantiate the `typeHint` directly
4. `FastActivator.Create<T>(abstractType)` fails because you cannot instantiate abstract classes

### Current Implementation (Broken)

**File:** `src/Vrb.Core/Utils/SerializationContextHelper.cs` (lines 45-51)
```csharp
// Add DummyDefinitionSerializationContext (required for definition references)
var dummyDefContextType = vrageLibrary.GetType(
    "Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
if (dummyDefContextType != null)
{
    var instance = Activator.CreateInstance(dummyDefContextType);
    if (instance != null) customContexts.Add(instance);
}
```

**Decompiled Engine File:** `decompiled/VRage.Library/Keen.VRage.Library.Definitions.Internal/DummyDefinitionSerializationContext.cs`
```csharp
public class DummyDefinitionSerializationContext : DefinitionSerializationContext
{
    public override bool TryLocateDefinition(Guid id, Type hintType, out Definition definition)
    {
        // PROBLEM: Creates placeholder using abstract hintType directly
        definition = DefinitionHelper.CreatePlaceholder(hintType, id);
        return true;
    }
}
```

---

## Solution: Load Definitions via DefinitionManager

The game engine properly resolves definitions using `DefinitionManager` which has all definitions pre-loaded with their concrete types. We need to replicate this initialization.

### How The Game Loads Definitions

The game uses a multi-step process:

1. **Load `definitionsets.vrb`** from game content folder
   - File: `{GamePath}/Content/definitionsets.vrb`
   - Contains: `DefinitionSetCollection` → `Dictionary<string, DefinitionList>`
   - Each `DefinitionList` maps GUIDs to `DefinitionLoadingData` (type info)

2. **Create a `DefinitionListLocator`** from the loaded data
   - Provides access to individual definition files via GUID

3. **Push definition set to `DefinitionManager`**
   - `Singleton<DefinitionManager>.Instance.PushDefinitionSetAsync(locator, setName, context)`
   - This loads and initializes all definitions

4. **During deserialization**, use `DefinitionLoaderContext` (not Dummy)
   - Has access to `DefinitionManager` and `DefinitionLoaderBase`
   - Resolves GUIDs to actual loaded `Definition` instances

### Key Decompiled Files to Reference

| File | Purpose |
|------|---------|
| `decompiled/VRage.Core/Keen.VRage.Core.EngineComponents/DefinitionSetManagerEngineComponent.cs` | Shows how game loads `definitionsets.vrb` and pushes definition sets |
| `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionListLocator.cs` | Locator that reads definition data |
| `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionLoaderBase.cs` | Contains `DefinitionLoaderContext` - the proper definition context |
| `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionLoader.cs` | Internal loader that processes definitions |
| `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionSetCollection.cs` | Container for definition sets |
| `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionSerializationContext.cs` | Base class for definition contexts |

---

## Implementation Plan

### Phase 1: Definition Set Loading Infrastructure

#### Task 1.1: Create `DefinitionSetLoader` Service
**New File:** `src/Vrb.Core/Core/DefinitionSetLoader.cs`

Responsibilities:
- Locate and parse `definitionsets.vrb` from game Content folder
- Deserialize `DefinitionSetCollection` using the engine's binary deserializer
- Cache the loaded `DefinitionList` data

Key implementation steps:
1. Find `definitionsets.vrb` in `{GamePath}/Content/` folder
2. Use `BinaryArchiveParser` to read the archive (similar to `DefinitionSetManagerEngineComponent.TryLoadDefinitionSetCollection`)
3. Deserialize into `DefinitionSetCollection` struct
4. Store the `Dictionary<string, DefinitionList>` for later use

Reference: `decompiled/VRage.Core/Keen.VRage.Core.EngineComponents/DefinitionSetManagerEngineComponent.cs` lines 289-337

```csharp
// Pseudocode based on game implementation
public class DefinitionSetLoader
{
    public DefinitionSetCollection? LoadDefinitionSets(string contentPath)
    {
        var setsFilePath = Path.Combine(contentPath, "definitionsets.vrb");
        using var stream = File.OpenRead(setsFilePath);
        var context = new SerializationContext(stream, "definitionsets.vrb");
        
        // Parse archive header
        var header = BinaryArchiveParser.TryReadHeader(stream, out var result);
        var parser = new BinaryArchiveParser(context);
        
        // Read type model and deserialize
        var typeModel = parser.ReadTypeModel(in header);
        return parser.DeserializeMainChunk<DefinitionSetCollection>(typeModel, in header);
    }
}
```

#### Task 1.2: Initialize `DefinitionManager` Singleton
**Modify:** `src/Vrb.Core/Core/GameEnvironmentInitializer.cs`

Currently initializes:
- MetadataManager ✓
- DefinitionManager post-processor cache (partial) ✓
- BumpAllocator ✓

Need to add:
- Push at least the "Core" or "Game" definition set to `DefinitionManager`

Key steps:
1. After MetadataManager initialization, call `DefinitionSetLoader.LoadDefinitionSets()`
2. Create a `DefinitionListLocator` from the loaded data
3. Call `Singleton<DefinitionManager>.Instance.PushDefinitionSetAsync(locator, "Core", null)`
4. Wait for the async operation to complete

### Phase 2: Custom Definition Serialization Context

#### Task 2.1: Create `VrbDefinitionSerializationContext`
**New File:** `src/Vrb.Core/Core/VrbDefinitionSerializationContext.cs`

A custom `DefinitionSerializationContext` that:
- First tries to resolve definitions from the loaded `DefinitionManager`
- Falls back to creating typed placeholders only if manager doesn't have it
- Handles abstract types by querying the manager for the actual type

```csharp
public class VrbDefinitionSerializationContext : DefinitionSerializationContext
{
    public override bool TryLocateDefinition(Guid id, Type hintType, out Definition definition)
    {
        // First: Try DefinitionManager (has concrete instances)
        if (Singleton<DefinitionManager>.Instance.TryGetDefinition(id, out definition))
        {
            return true;
        }
        
        // Second: Try to get loading data to find concrete type
        if (Singleton<DefinitionManager>.Instance.TryGetDefinitionLoadingData(id, out var data))
        {
            // data.Type is the CONCRETE type, not the abstract hint
            definition = FastActivator.Instance.Create<Definition>(data.Type);
            definition.SetGuid(id);
            return true;
        }
        
        // Fallback: Only if type is not abstract
        if (!hintType.IsAbstract)
        {
            definition = FastActivator.Instance.Create<Definition>(hintType);
            definition.SetGuid(id);
            return true;
        }
        
        // Cannot resolve - log warning
        definition = null;
        return false;
    }
}
```

#### Task 2.2: Update `SerializationContextHelper` 
**Modify:** `src/Vrb.Core/Utils/SerializationContextHelper.cs`

Replace `DummyDefinitionSerializationContext` with `VrbDefinitionSerializationContext`:

```csharp
// BEFORE (broken):
var dummyDefContextType = vrageLibrary.GetType(
    "Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");

// AFTER (working):
// Use our custom context that properly resolves from DefinitionManager
customContexts.Add(new VrbDefinitionSerializationContext());
```

### Phase 3: Content File Access

#### Task 3.1: Implement Content File Resolution
**New File:** `src/Vrb.Core/Infrastructure/ContentFileResolver.cs`

The game's `DefinitionListLocator.GetDefinitionContext()` reads definition files from the content cache. We need to replicate this:

1. Definitions are stored as individual binary files in the content folder
2. Each definition's GUID maps to a file via `ResourceHandle`
3. The game uses `FileSystem.ContentCache` to resolve paths

Options:
- **Option A:** Initialize the game's `FileSystem` and `ContentCache` (complex)
- **Option B:** Build our own GUID→file mapping from the definition set data (simpler)
- **Option C:** Skip loading individual definitions, only use the DefinitionManager's type registry for abstract type resolution (simplest for our use case)

**Recommended:** Option C for initial implementation
- We only need to know the **concrete type** for each definition GUID
- The `DefinitionLoadingData` from `definitionsets.vrb` contains `Type` info
- We don't need the full definition data, just enough to create empty instances

### Phase 4: Integration & Testing

#### Task 4.1: Update Initialization Flow
**Modify:** `src/Vrb.Core/VrbSerializationSetup.cs`

```csharp
public static void InitializeVrb(this IServiceProvider serviceProvider, string? gameInstallPath = null)
{
    // ... existing code ...
    
    // 2. Load Assemblies
    GameAssemblyManager.LoadAssemblies(gameInstallPath, logger);

    // 3. Initialize Game Environment (existing)
    var initializer = serviceProvider.GetRequiredService<GameEnvironmentInitializer>();
    initializer.Initialize();
    
    // 4. NEW: Load Definition Sets
    var contentPath = Path.Combine(gameInstallPath, "Content");
    var defSetLoader = serviceProvider.GetRequiredService<DefinitionSetLoader>();
    defSetLoader.LoadAndPushDefinitions(contentPath);
}
```

#### Task 4.2: Update Tests
**Modify:** `tests/Vrb.Tests/LibraryTests.cs`

- Ensure `VrbToJson_SessionComponents_ReturnsValidJson` passes
- Ensure `RoundTrip_SessionComponents_Succeeds` passes
- Add specific test for abstract definition reference handling

---

## Detailed File References

### DefinitionSetManagerEngineComponent (Game's Implementation)

**File:** `decompiled/VRage.Core/Keen.VRage.Core.EngineComponents/DefinitionSetManagerEngineComponent.cs`

Key constants:
```csharp
public const string SETS_FILENAME = "definitionsets.vrb";
```

Key methods:
- `TryLoadDefinitionSetCollection(VRageProject project)` - lines 289-337
- `PushDefinitionSetAsync(string setName)` - lines 71-79

### DefinitionLoaderContext (Proper Context Implementation)

**File:** `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionLoaderBase.cs` lines 26-63

```csharp
protected class DefinitionLoaderContext : DefinitionSerializationContext
{
    public override bool TryLocateDefinition(Guid id, Type hintType, out Definition definition)
    {
        // Uses _manager.TryGetDefinition() first
        // Then falls back to _loader.TryGetInstance()
        if (!_manager.TryGetDefinition(id, out definition) && 
            !_loader.TryGetInstance(id, out definition))
        {
            return false;
        }
        // ... dependency tracking ...
        return true;
    }
}
```

### DefinitionLoadingData Structure

**File:** `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionLoadingData.cs`

Contains:
- `Guid` - Definition identifier
- `Type` - **Concrete** definition type (not abstract!)
- `IsAbstract` - Whether this is an abstract definition
- `PartialDefinitions` - List of partial definition GUIDs
- `PriorityOverrides` - Override definitions

### FastActivator Usage in Loader

**File:** `decompiled/VRage.Library/Keen.VRage.Library.Definitions/DefinitionLoaderBase.cs` lines 335-358

```csharp
protected bool TryGetInstance(Guid id, out Definition definition)
{
    // Uses value.Type from DefinitionLoadingData
    // This is ALWAYS the concrete type, never abstract
    definition = FastActivator.Instance.Create<Definition>(value.Type);
    definition.SetGuid(id);
    return true;
}
```

---

## Risk Assessment

| Risk | Mitigation |
|------|------------|
| `definitionsets.vrb` format changes between game versions | Version check against known formats; graceful fallback |
| ContentCache initialization is complex | Start with Option C (type registry only); expand later if needed |
| DefinitionManager requires more subsystems | Isolate minimal required initialization; mock where possible |
| Performance impact of loading all definitions | Lazy loading; only load definition types on demand |

---

## Success Criteria

1. ✅ `dotnet test --filter "SessionComponents"` passes
2. ✅ Round-trip validation works for `sessioncomponents.vrb`
3. ✅ No regression in `savegame.vrb` handling
4. ✅ Abstract definition types (e.g., `ProgressionCheckpointDefinition`) resolve correctly
5. ✅ Tool startup time remains reasonable (< 5 seconds)

---

## Appendix: Abstract Definition Types in SessionComponents

Types known to cause issues:
- `Keen.Game2.Simulation.GameSystems.Progression.ProgressionCheckpointDefinition` (abstract)
  - Concrete subclass: `ProductionBlockProgressionCheckpointDefinition`
- Potentially others in player/session data

The fix ensures we always resolve to concrete types from `DefinitionLoadingData.Type` rather than relying on the serializer's `hintType` parameter.

