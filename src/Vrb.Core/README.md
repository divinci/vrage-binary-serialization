# Bjornabe.Vrb.Core

A .NET library for serializing and deserializing **Space Engineers 2** binary save files (`.vrb`) to and from JSON.

This library enables modders and tool developers to programmatically read, modify, and write SE2 save data with full compatibility with the game's binary format.

## Prerequisites

- **OS:** Windows 10/11 (64-bit)
- **Runtime:** .NET 9.0
- **Game:** Space Engineers 2 installed via Steam (assemblies are loaded at runtime)

## Installation

```bash
dotnet add package Bjornabe.Vrb.Core
```

## Usage

```csharp
using Vrb.Core;

// 1. Initialize (once at startup)
// Optionally pass a custom game path; otherwise it auto-discovers SE2 via Steam
Vrb.Initialize();

// 2. Convert VRB to JSON
var json = Vrb.Service!.DeserializeVrb("savegame.vrb", TargetType.SaveGame);

// 3. Convert JSON to VRB
Vrb.Service.SerializeJsonToVrb(json, "savegame_modified.vrb", TargetType.SaveGame);
```

### Supported File Types

| TargetType | File | Description |
|------------|------|-------------|
| `SaveGame` | `savegame.vrb` | Entity bundle containing all entities in a save |
| `SessionComponents` | `sessioncomponents.vrb` | Session component state snapshot |
| `AssetJournal` | `assetjournal.vrb` | Asset journal data |
| `DefinitionSets` | `definitionsets.vrb` | Game definition set collection |

### Custom Game Path

If the game is not installed via Steam or you need to specify a custom path:

```csharp
Vrb.Initialize(@"C:\Games\SpaceEngineers2\Game2");
```

### Compression Options

When writing VRB files, you can specify the compression method:

```csharp
Vrb.Service.SerializeJsonToVrb(json, "output.vrb", TargetType.SaveGame, CompressionMethod.Brotli);
```

Available compression methods: `None`, `ZLib`, `Brotli` (default matches game behavior).

## How It Works

The library uses **Space Engineers 2's own serialization assemblies** loaded at runtime, ensuring perfect compatibility with the game's binary format. This means:

- No reverse-engineered formats that could break with game updates
- Full support for all data types the game uses
- Lossless round-trip conversion (VRB → JSON → VRB)

## JSON Output Format

The JSON output includes engine-compatible metadata:

```json
{
  "$Bundles": {
    "Game2": "2.0.2.47",
    "VRage": "2.0.2.47"
  },
  "$Type": "VRage:Keen.VRage.Core.Game.Systems.EntityBundle",
  "$Value": {
    "Roots": 210,
    "Entities": [0, 1, 2],
    "Builders": [...]
  }
}
```

## Additional Resources

- [Full Documentation & CLI Tool](https://github.com/divinci/vrage-binary-serialization)
- [Report Issues](https://github.com/divinci/vrage-binary-serialization/issues)

## Disclaimer

This is an unofficial community tool, not affiliated with or endorsed by Keen Software House. Always backup your save files before editing. Use at your own risk.
