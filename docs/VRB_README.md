# VRage Binary Format (VR3B) Analysis Report

This report documents the internal structure and serialization mechanism of the `.vrb` files (VRage Binary Format) used in Space Engineers 2, based on the decompiled source code.

## 1. Overview

The `.vrb` format is a custom binary archive format designed by Keen Software House for Space Engineers 2. It is identified by the magic bytes `VR3B`. The format supports compression (ZLib, Brotli), checksum validation (XxHash64), and modular data chunks. It is primarily handled by the `Keen.VRage.Library.Serialization.Binary` namespace.

## 2. File Structure

The file consists of a fixed-size header followed by variable-length data sections (tables and chunks).

### 2.1. Archive Header (`ArchiveHeader`)

The header is **192 bytes** long and has the following layout:

| Offset | Type | Name | Description |
| :--- | :--- | :--- | :--- |
| 0 | `uint` | `Magic` | File signature. Must be `0x42335256` ("VR3B" in Little Endian). |
| 4 | `uint` | `Version` | Format version. Currently `1`. |
| 8 | `ulong` | `Checksum` | XxHash64 checksum of the header itself (with this field set to 0). |
| 16 | `ArchiveEntry` | `BundleTable` | Metadata for the Bundle Table. |
| 52 | `ArchiveEntry` | `TypeTable` | Metadata for the Type Table (Reflection data). |
| 88 | `ArchiveEntry` | `ChunkTable` | Metadata for the Chunk Table. |
| 124 | `int` | `BundleCount` | Number of bundles in the Bundle Table. |
| 128 | `int` | `ChunkCount` | Number of chunks in the Chunk Table. |
| 132 | `ChunkArchiveEntry` | `MainChunk` | Metadata for the primary data chunk (e.g., the save game data). |

### 2.2. Archive Entry (`ArchiveEntry`)

Used to describe a section of data in the file. Size: **36 bytes**.

| Offset | Type | Name | Description |
| :--- | :--- | :--- | :--- |
| 0 | `long` | `Offset` | Byte offset of the data from the **start of the file**. |
| 8 | `ulong` | `Checksum` | XxHash64 checksum of the *compressed* data on disk. |
| 16 | `long` | `EntrySize` | Size of the data on disk (compressed size). |
| 24 | `long` | `UncompressedSize` | Size of the data after decompression. |
| 32 | `byte` | `CompressionType` | Compression method used. |
| 33 | `byte[3]` | `Padding` | 3 bytes of padding. |

### 2.3. Chunk Archive Entry (`ChunkArchiveEntry`)

Extends `ArchiveEntry` to include specific chunk metadata. Size: **40 bytes**.

| Offset | Type | Name | Description |
| :--- | :--- | :--- | :--- |
| 0-35 | `ArchiveEntry` | (Base) | Inherited fields from `ArchiveEntry`. |
| 36 | `bool` | `DeltaEncoded` | Whether the chunk uses delta encoding. |
| 37 | `byte[2]` | `Padding` | 2 bytes of padding. |
| 39 | `NameIndex` | `RootType` | Index or identifier for the root type of the chunk. |

### 2.4. Compression Types

Defined in `CompressionType` enum:
*   `0`: **None** (Raw data)
*   `1`: **ZLib** (Deflate)
*   `2`: **Brotli**

## 3. Data Sections

The file contains three main tables and a variable number of data chunks.

### 3.1. Bundle Table
*   **Purpose**: Lists the assembly bundles required to deserialize the data.
*   **Format**:
    *   List of `BundleInfo` structs.
    *   Each `BundleInfo` consists of a String (Name) and a Version.

### 3.2. Type Table
*   **Purpose**: Contains reflection data to map serialized IDs to C# types.
*   **Format**: `RuntimeTypeModel` serialized via `VRageBinaryReader`.
*   **Mechanism**: Allows the deserializer to reconstruct the object graph even if types have changed slightly, by mapping field names and types.

### 3.3. Chunk Table
*   **Purpose**: Index of additional data chunks available in the file.
*   **Format**:
    *   List of Key-Value pairs: `KeyValuePair<string, ChunkArchiveEntry>`.
    *   Key: String identifier for the chunk.
    *   Value: `ChunkArchiveEntry` struct describing the chunk's location and properties.

### 3.4. Main Chunk (`[Main]`)
*   **Purpose**: The entry point for the actual game data (e.g., `EntityBundle`).
*   **Access**: Pointed to directly by the `MainChunk` field in the Header.
*   **Content**: Serialized object graph of the root type (e.g., `EntityBundle` for `savegame.vrb`).

## 4. Object Graph & Serialization Logic

### 4.1. The Object Graph
The object graph is **generic** but deeply structured around the C# type system. It is not a rigid, pre-defined binary struct but rather a serialized representation of the runtime object hierarchy.

*   **Polymorphism**: The system handles polymorphic types. The `TypeTable` stores metadata about every type encountered in the graph.
*   **Root Object**: For `savegame.vrb`, the root object is `EntityBundle`.
    *   `EntityBundle` contains a list of `EntityObjectBuilder` (the persistent state of entities).
    *   It also contains `Entity` references, though these are often transient or reconstructed from builders.
*   **Members**: Fields and properties are serialized based on attributes like `[Serialize]`.
    *   **Attributes**: `[Serialize]`, `[NoSerialize]`, `[SerializerBundle]`.
    *   **SerializationFlags**: Control behavior like `NestedSharedReference`.

### 4.2. Reading Process (`BinaryArchiveParser`)
1.  **Read Header**: Read the first 192 bytes.
2.  **Validate**:
    *   Check `Magic` == `VR3B`.
    *   Calculate XxHash64 of the header bytes (with Checksum=0) and compare with `Header.Checksum`.
3.  **Read Tables**:
    *   Read `BundleTable` using `ArchiveEntry` info.
    *   Read `TypeTable` and construct `RuntimeTypeModel`.
4.  **Read Main Chunk**:
    *   Locate `MainChunk` using header info.
    *   Decompress if necessary (`ZLib` or `Brotli`).
    *   Deserialize the object graph using `BinaryDeserializationContext` and the `RuntimeTypeModel`.

### 4.3. Primitive Types (`VRageBinaryReader`)
*   **Numbers**: Read directly as little-endian (e.g., `int`, `float`, `long`).
*   **Strings**:
    *   **UTF-16**: Length (`int32`) + Bytes (Length * 2).
    *   **UTF-8**: Length (`int32`) + Bytes.

### 4.4. KeyedList Serialization (`KeyedList<TKey, TValue>`)
The `KeyedList<TKey, TValue>` is a custom collection type used extensively in the engine.

*   **Structure**: It is serialized as a sequence of `KeyedCollectionPair<TKey, TValue>` items.
*   **Item Format (`KeyedCollectionPair`)**:
    1.  **Key**: Serialized using the generic serializer for `TKey`.
    2.  **Value**: Serialized using the generic serializer for `TValue`.
*   **Binary Layout**:
    1.  **Length**: A "compact optional integer" representing the number of items.
    2.  **Items**: The list of pairs follows immediately.
*   **Delta Encoding**: It supports delta encoding (saving only changes), but the binary implementation currently throws `NotImplementedException` for `ValuePrefix.DeltaEncoded`, suggesting it defaults to full serialization in binary mode or falls back to a different path for delta updates.

## 5. File Types

*   **`savegame.vrb`**: Root type is `Keen.VRage.Core.Game.Systems.EntityBundle`.
    *   Contains `Builders` (List of `EntityObjectBuilder`).
    *   Contains `Roots` (Count of root entities).
*   **`sessioncomponents.vrb`**: Contains session-level components (GameMode, Settings, Player Data).
*   **`assetjournal.vrb`**: Contains asset streaming data.
*   **`definitionsets.vrb`**: Root type is `Keen.VRage.Library.Definitions.DefinitionSetCollection`.
    *   Contains game definitions (blocks, items, components, etc.).
    *   Located in the game installation directory under `GameData/` and `VRage/GameData/`.

## 6. Implementation Notes

*   **Hashing**: Uses `XxHash64` seeded with the Magic value (`0x42335256`).
*   **Serialization Context**: Relies heavily on `SerializationContext` to manage state during deep object graph deserialization.
*   **Attributes**: Uses `[VR3BAsset]` attribute to mark compatible assets.

