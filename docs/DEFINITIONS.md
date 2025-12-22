# Definitions in Space Engineers 2

This document provides an in-depth explanation of the Definition system in Space Engineers 2, based on analysis of the decompiled game engine source code.

## 1. Overview

**Definitions** are the core data-driven content system in Space Engineers 2. They represent **immutable, game-wide templates** that define the properties, behaviors, and characteristics of game content. Think of them as "blueprints" or "prototypes" that describe what something *is* rather than a specific instance of it.

### Key Characteristics

- **Immutable**: Once loaded, definitions don't change during gameplay
- **GUID-based**: Each definition has a unique `Guid` identifier
- **Type-safe**: Each definition is a strongly-typed C# class inheriting from `Definition`
- **Content-driven**: Definitions are loaded from game content files, not hardcoded
- **Serializable**: Definitions can be serialized/deserialized using the VRB format

## 2. What Definitions Represent

Definitions serve as the **data layer** for virtually all game content:

### 2.1. Game Content Examples

- **Block Definitions**: Properties of buildable blocks (e.g., `HingeDefinition`, `PlacementDefinition`)
- **Component Definitions**: Server/client component configurations (e.g., `ResourceContainerInventorySyncServerDefinition`)
- **Progression Definitions**: Player progression checkpoints and unlocks (e.g., `ProductionBlockProgressionCheckpointDefinition`)
- **Mission Definitions**: Mission templates and objectives
- **Resource Definitions**: Resource types and properties
- **Localization Definitions**: Language and culture settings (e.g., `CultureInfoDefinition`)
- **Configuration Definitions**: Game system configurations

### 2.2. The Definition Hierarchy

All definitions inherit from the base `Definition` class:

```csharp
public class Definition : ISerializableNamedObject<Guid>, ICloneableNamedObject, IDisposable
{
    public Guid Guid { get; private set; }
    public string DebugName { get; private set; }
    // ... other base properties
}
```

Concrete definition types extend this base class:

```csharp
// Abstract base for progression checkpoints
public abstract class ProgressionCheckpointDefinition : Definition
{
    public LocKey DisplayString { get; private set; }
    public LocKey DisplayStringConcise { get; private set; }
}

// Concrete implementation
public class ProductionBlockProgressionCheckpointDefinition : ProgressionCheckpointDefinition
{
    // Specific properties for production block progression
}
```

## 3. Definition Architecture in the Game

### 3.1. DefinitionManager

The `DefinitionManager` is the central singleton that manages all definitions in the game. It provides:

- **Definition Storage**: Maintains a registry of all loaded definitions indexed by GUID
- **Definition Loading**: Coordinates loading definitions from various sources
- **Definition Lookup**: Provides `TryGetDefinition<T>(Guid, out T)` methods
- **Definition Reloading**: Supports hot-reloading definitions during development
- **Dependency Tracking**: Tracks relationships between definitions

### 3.2. Definition Loading Pipeline

Definitions are loaded through a multi-stage pipeline:

1. **Discovery**: `IDefinitionObjectBuilderLocator` implementations discover definition metadata
2. **Loading**: `DefinitionLoader` loads the actual definition data
3. **Initialization**: Definitions are initialized via `Init(DefinitionObjectBuilder)`
4. **Post-Processing**: `IDefinitionPostProcessor` implementations apply transformations
5. **Validation**: Definitions are validated for correctness
6. **Registration**: Definitions are registered in `DefinitionManager`

### 3.3. Definition Sources

Definitions can come from multiple sources:

- **`definitionsets.vrb`**: Binary archives containing definition metadata and data
- **Embedded Resources**: Definitions embedded in assemblies
- **Content Cache**: Cached definitions from previous loads
- **In-Memory**: Definitions created programmatically (for testing/modding)

### 3.4. Definition Sets

Definitions are organized into **Definition Sets** (e.g., "World", "Core", "Engine"). Each set contains:

- A `Dictionary<Guid, DefinitionLoadingData>` mapping GUIDs to type information
- The actual definition data (serialized as VRB)
- Metadata about partial definitions, overrides, and dependencies

## 4. DefinitionLoadingData

`DefinitionLoadingData` is a metadata structure that describes how to load a definition:

```csharp
public readonly struct DefinitionLoadingData
{
    public Guid Guid { get; }                    // Unique identifier
    public Type Type { get; }                    // Concrete C# type
    public DefinitionFlags Flags { get; }        // Flags (Abstract, SkipValidation, etc.)
    public PartialDefinitionKind PartialKind { get; }  // Override, Copy, etc.
    public Guid BaseGuid { get; }                // Parent definition (for partials)
    public ImmutableArray<Guid> PriorityOverrides { get; }
    public ImmutableArray<Guid> PartialDefinitions { get; }
}
```

### Key Properties

- **`Type`**: The **concrete** C# type to instantiate (critical for deserialization)
- **`IsAbstract`**: Whether the definition type is abstract (cannot be instantiated directly)
- **`IsInstance`**: Whether this definition will be instantiated (not abstract, not override)

## 5. Definitions in VRB Serialization/Deserialization

### 5.1. The Problem

When deserializing VRB files, the engine encounters **definition references** - GUIDs that point to definitions. The serialization system needs to:

1. **Resolve the GUID** to the actual `Definition` instance
2. **Handle abstract types**: Some references use abstract base types (e.g., `ProgressionCheckpointDefinition`)
3. **Instantiate correctly**: Create the correct concrete type, not the abstract type

### 5.2. DefinitionSerializationContext

The `DefinitionSerializationContext` is a custom serialization context that handles definition resolution:

```csharp
public abstract class DefinitionSerializationContext : CustomSerializationContext
{
    public abstract bool TryLocateDefinition(
        Guid id, 
        Type hintType, 
        out Definition definition
    );
}
```

During deserialization:

1. The serializer encounters a definition reference (GUID + type hint)
2. It calls `TryLocateDefinition(id, hintType, out definition)`
3. The context looks up the definition and returns it
4. If the definition doesn't exist, it may create a placeholder or throw

### 5.3. The Abstract Type Challenge

**Problem**: When `hintType` is abstract (e.g., `ProgressionCheckpointDefinition`), the system cannot directly instantiate it.

**Example from SessionComponents**:
```csharp
public class PlayerProgressionData
{
    public ObservableCollection<ProgressionCheckpointDefinition> UnlockedRules { get; }
    // ↑ This is an abstract type!
}
```

When deserializing, the serializer sees:
- GUID: `c7b8e669-e76b-4edb-aa18-8067d0340b96`
- Hint Type: `ProgressionCheckpointDefinition` (abstract)

The system needs to:
1. Look up the GUID in the definition registry
2. Find the **concrete type** (e.g., `ProductionBlockProgressionCheckpointDefinition`)
3. Return an instance of the concrete type

### 5.4. DummyDefinitionSerializationContext

The game provides a `DummyDefinitionSerializationContext` for testing/placeholder purposes:

```csharp
public class DummyDefinitionSerializationContext : DefinitionSerializationContext
{
    public override bool TryLocateDefinition(Guid id, Type hintType, out Definition definition)
    {
        definition = DefinitionHelper.CreatePlaceholder(hintType, id);
        return true;
    }
}
```

**Limitation**: This always uses `hintType`, which fails when `hintType` is abstract because `CreatePlaceholder` cannot instantiate abstract classes.

### 5.5. The Real Solution: DefinitionManager Integration

The proper solution (used by the game itself) is to:

1. **Load all definitions** from `definitionsets.vrb` files
2. **Build a GUID → Concrete Type mapping** (via `DefinitionLoadingData`)
3. **Use the concrete type** when creating placeholders or resolving references

This is what our `DefinitionsHelper` and `SmartDefinitionSerializationContext` implement.

## 6. How This Tool Uses Definitions

### 6.1. DefinitionsHelper

Our `DefinitionsHelper` class:

1. **Loads Definition Sets**: Reads `definitionsets.vrb` from:
   - `GameData\Vanilla\Content\definitionsets.vrb`
   - `VRage\GameData\Engine\Content\definitionsets.vrb`

2. **Extracts Type Information**: Builds a `Dictionary<Guid, Type>` mapping:
   ```csharp
   // For each definition in each set:
   var guid = /* from DictionaryReader.Keys */;
   var loadingData = definitionsReader[guid];
   var concreteType = loadingData.Type;  // The actual concrete type!
   _definitionTypes[guid] = concreteType;
   ```

3. **Provides Lookup**: Offers `TryGetDefinitionType(Guid, out Type)` to get concrete types

### 6.2. SmartDefinitionSerializationContext

Our `SmartDefinitionSerializationContext`:

1. **Dynamically Generates** a subclass of `DefinitionSerializationContext` using `System.Reflection.Emit`
2. **Overrides `TryLocateDefinition`** to:
   - Look up the GUID in `DefinitionsHelper`
   - Get the **concrete type** (not the abstract hint type)
   - Call `CreatePlaceholder` with the concrete type
   - Return the created definition

3. **Falls Back Gracefully**: If lookup fails, uses the hint type (if not abstract)

### 6.3. Integration with SerializationContextHelper

`SerializationContextHelper` now:

1. **Creates Smart Context**: Attempts to create `SmartDefinitionSerializationContext`
2. **Falls Back**: Uses `DummyDefinitionSerializationContext` if smart context creation fails
3. **Applies to Both**: Used for both binary (VRB) and JSON serialization contexts

## 7. Definition Storage: definitionsets.vrb

### 7.1. File Structure

`definitionsets.vrb` files are VRB archives containing:

- **DefinitionSetCollection**: Root object
  - **DefinitionSets**: `Dictionary<string, DefinitionList>`
    - Key: Set name (e.g., "World", "Core", "Engine")
    - Value: `DefinitionList`
      - **Definitions**: `DictionaryReader<Guid, DefinitionLoadingData>`
        - Maps GUIDs to type information

### 7.2. Loading Process

1. Deserialize `DefinitionSetCollection` using `BinaryArchiveReader.ReadMainChunk<T>()`
2. Iterate over `DefinitionSets` dictionary
3. For each `DefinitionList`, get the `Definitions` property
4. Iterate over `DictionaryReader.Keys` (which implements `IEnumerable<Guid>`)
5. For each GUID, use the indexer to get `DefinitionLoadingData`
6. Extract the `Type` property (the concrete C# type)
7. Store in `Dictionary<Guid, Type>`

### 7.3. Statistics

From a typical game installation:
- **Vanilla Content**: ~2 definition sets, thousands of definitions
- **Engine Content**: ~3 definition sets, additional definitions
- **Total**: ~11,428 definitions loaded

## 8. Definition Lifecycle

### 8.1. At Game Startup

1. Game locates `definitionsets.vrb` files
2. `DefinitionSetManagerEngineComponent` loads definition sets
3. `DefinitionLoader` processes each definition:
   - Deserializes `DefinitionObjectBuilder` from VRB
   - Creates instance of concrete type
   - Calls `Init(builder)`
   - Applies post-processors
   - Validates
   - Registers in `DefinitionManager`

### 8.2. During Serialization

1. When serializing, definitions are referenced by GUID
2. `ISerializableNamedObject<Guid>` interface provides `GetName()` → returns GUID
3. Serializer writes GUID instead of full object

### 8.3. During Deserialization

1. Serializer reads GUID
2. Looks up `DefinitionSerializationContext` in `SerializationContext`
3. Calls `TryLocateDefinition(guid, hintType, out definition)`
4. Context resolves GUID to actual definition instance
5. Returns definition (or placeholder if not found)

## 9. Advanced Topics

### 9.1. Partial Definitions

Definitions support **partial definitions** - modifications or extensions:

- **Overrides**: Modify an existing definition
- **Copies**: Create new definitions based on existing ones
- **Priority Overrides**: Overrides that take precedence

### 9.2. Definition Flags

`DefinitionFlags` enum includes:
- `Abstract`: Definition type is abstract (cannot be instantiated)
- `SkipValidation`: Skip validation for this definition
- Other flags for special behaviors

### 9.3. Definition Post-Processors

`IDefinitionPostProcessor` implementations can:
- Transform definition data after loading
- Validate definitions
- Build derived data structures
- Apply game-specific logic

Example: `BlockProgressionCheckpointProcessor` processes progression-related definitions.

### 9.4. Definition Validation

Definitions are validated after loading:
- Type checking
- Required field validation
- Cross-reference validation
- Business rule validation

## 10. Practical Implications for This Tool

### 10.1. Why Definitions Matter

Without proper definition loading:
- ❌ Abstract types cannot be instantiated → deserialization fails
- ❌ Definition references cannot be resolved → incomplete object graphs
- ❌ Type information is lost → incorrect JSON output

With proper definition loading:
- ✅ All definition references resolve correctly
- ✅ Abstract types resolve to concrete implementations
- ✅ Complete, accurate JSON output
- ✅ Round-trip serialization works

### 10.2. Current Implementation Status

✅ **Complete**: All 30 tests pass, including SessionComponents tests

- `DefinitionsHelper` loads 11,428 definitions from game files
- `SmartDefinitionSerializationContext` resolves abstract types correctly
- All VRB file types (SaveGame, SessionComponents, AssetJournal, DefinitionSets) work

### 10.3. Future Enhancements

Potential improvements:
- Load actual definition instances (not just type info) for more accurate resolution
- Support definition reloading during development
- Cache definition data for faster subsequent loads
- Support mod definitions (if modding API is available)

## 11. References

### Key Classes (from decompiled source)

- `Keen.VRage.Library.Definitions.Definition` - Base class
- `Keen.VRage.Library.Definitions.DefinitionManager` - Central manager
- `Keen.VRage.Library.Definitions.DefinitionLoadingData` - Metadata structure
- `Keen.VRage.Library.Definitions.DefinitionSerializationContext` - Serialization context
- `Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext` - Placeholder context
- `Keen.VRage.Library.Definitions.DefinitionSetCollection` - Definition set container
- `Keen.VRage.Library.Definitions.DefinitionList` - List of definitions in a set

### Key Files in This Tool

- `src/Vrb.Core/Utils/DefinitionsHelper.cs` - Definition loading and lookup
- `src/Vrb.Core/Utils/SmartDefinitionSerializationContext.cs` - Smart context implementation
- `src/Vrb.Core/Utils/SerializationContextHelper.cs` - Context creation
- `tests/Vrb.Tests/DefinitionHelperTests.cs` - Definition loading tests

---

*Document generated from analysis of Space Engineers 2 decompiled source code and tool implementation.*


