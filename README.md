# VRB Serialization Tool (Space Engineers 2)

[![NuGet](https://img.shields.io/nuget/v/Bjornabe.Vrb.Core.svg?style=flat-square&logo=nuget&label=Bjornabe.Vrb.Core)](https://www.nuget.org/packages/Bjornabe.Vrb.Core/)

**NuGet Package:** [`Bjornabe.Vrb.Core`](https://www.nuget.org/packages/Bjornabe.Vrb.Core/) (Version matches SE2 Build, e.g., `21190635.0.0`)

A modding utility for **Space Engineers 2** that allows you to serialize and deserialize binary save files (`.vrb`) to and from human-readable JSON. This enables players and modders to edit save data, inspect internal game structures, and create external tools that interact with SE2 save files.

The tool leverages the game's own assemblies (`VRage.Library`, `Game2.Simulation`, etc.) to ensure accurate data processing and full compatibility with the game's binary format.

**Current Platform Support:** Windows Only (due to game assembly dependencies).

## Features

- **Bidirectional Conversion:**
  - **VRB to JSON:** Convert binary save files to structured JSON for easy editing in any text editor.
  - **JSON to VRB:** Rehydrate your modified JSON back into a valid `.vrb` file that the game can load.
- **Validation:** Performs a lossless round-trip check on a `.vrb` file (`VRB -> JSON -> VRB`) to ensure your data is preserved byte-for-byte.
- **Smart Detection:** Automatically identifies the internal structure of `.vrb` files via brute-force type checking and `.json` files via content inspection.
- **Auto-Detection:** Automatically locates the Space Engineers 2 installation path via Steam registry keys or library folders.
- **Library:** Can be referenced by other C#/.NET 9 applications to add VRB support to your own tools.
- **Supported File Types:**
  - `savegame.vrb` (Entity Bundle)
  - `sessioncomponents.vrb` (Session Components Snapshot)
  - `assetjournal.vrb` (Asset Journal)

## Prerequisites

- **OS:** Windows 10/11 (64-bit)
- **Runtime:** .NET 9.0 Runtime (or SDK to build)
- **Game:** Space Engineers 2 installed (via Steam)

## Installation & Build

Currently, the tool must be built from source.

1.  **Clone the repository:**
    ```bash
    git clone https://github.com/yourusername/vrage-binary-serialization.git
    cd vrage-binary-serialization
    ```

2.  **Build the project:**
    ```bash
    dotnet build vrb.sln -c Release
    ```

3.  **Locate the artifacts:**
    - **CLI Tool:** `src/Vrb/bin/Release/net9.0-windows/vrb.exe`
    - **Library:** `src/Vrb.Core/bin/Release/net9.0-windows/Vrb.Core.dll`

## CLI Usage

The Command Line Interface (CLI) is the primary way to use the tool. It accepts one or two file paths as arguments.

### 1. Convert VRB to JSON
Provide the source `.vrb` file and the destination `.json` file.

```bash
# Syntax: vrb.exe <input.vrb> <output.json>
vrb.exe "C:\Users\You\AppData\Roaming\SpaceEngineers2\SaveGames\MySave\savegame.vrb" "savegame.json"
```

### 2. Convert JSON to VRB
Provide the source `.json` file and the destination `.vrb` file.

```bash
# Syntax: vrb.exe <input.json> <output.vrb>
vrb.exe "savegame.json" "savegame.vrb"
```

### 3. Validate Conversion (Lossless Check)
Provide a single `.vrb` file path to perform a lossless round-trip check (`VRB -> JSON -> VRB`). It compares the binary hash of the re-created file against the original to ensure data integrity.

```bash
# Syntax: vrb.exe <input.vrb>
vrb.exe "savegame.vrb"
```

> **Note on Smart Detection:** If you provide files with generic names (e.g., `data.vrb`), the tool will automatically attempt to detect the correct game structure by testing all known types or inspecting JSON metadata.

## Library Usage (C# / .NET)

You can use the core logic in your own .NET 9 applications by referencing the **Vrb.Core** library.

### 1. Add Reference
Add a reference to the `Vrb.Core` project or NuGet package.

### 2. Initialization & Usage
Use the simplified static initializer to setup the environment and start converting files.

```csharp
using Vrb.Core;

// 1. Initialize (once at startup)
// Optionally pass a path; otherwise it auto-discovers SE2
Vrb.Initialize();

// 2. Convert VRB -> JSON (returns JSON string)
// Use the ! operator or check for null if you are sure it's initialized
var json = Vrb.Service!.DeserializeVrb("savegame.vrb", TargetType.SaveGame);

// 3. Convert JSON -> VRB
Vrb.Service.SerializeJsonToVrb(json, "savegame_new.vrb", TargetType.SaveGame);
```

## Architecture

### How It Works

The tool uses the **game engine's own serialization libraries** for both binary (VRB) and JSON formats, ensuring perfect compatibility:

```
┌─────────────────────────────────────────────────────────────┐
│                    VrbProcessingService                      │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│   DeserializeVrb(path, type)         VRB → JSON              │
│   ┌────────────┐     ┌────────────────┐     ┌─────────────┐ │
│   │ .vrb file  │ ──▶ │ Object Graph   │ ──▶ │ JSON string │ │
│   └────────────┘     └────────────────┘     └─────────────┘ │
│        │                    │                      │         │
│   BinaryArchiveReader  (game engine)    SerializationHelper  │
│                                              (Format.Json)   │
│                                                              │
│   SerializeJsonToVrb(json, path, type)   JSON → VRB          │
│   ┌─────────────┐     ┌────────────────┐     ┌────────────┐ │
│   │ JSON string │ ──▶ │ Object Graph   │ ──▶ │ .vrb file  │ │
│   └─────────────┘     └────────────────┘     └────────────┘ │
│        │                    │                      │         │
│   SerializationHelper  (game engine)   BinaryArchiveWriter   │
│   (Format.Json)                          (with compression)  │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

### JSON Output Format

The JSON output includes engine-compatible metadata for proper deserialization:

```json
{
  "$Bundles": {
    "Game2": "2.0.2.47",
    "VRage": "2.0.2.47"
  },
  "$Type": "VRage:Keen.VRage.Core.Game.Systems.EntityBundle",
  "$Value": {
    "Roots": 210,
    "Entities": [0, 1, 2, ...],
    "Builders": [...]
  }
}
```

### Project Structure

*   **`src/Vrb`**: The Console CLI application entry point.
*   **`src/Vrb.Core`**: The reusable Class Library containing all logic.
    *   **`Core`**: Core logic including `VrbProcessingService` (Orchestrator) and `GameEnvironmentInitializer`.
    *   **`Infrastructure`**: Handles external concerns like finding the game path (`GameInstallLocator`) and loading assemblies (`GameAssemblyManager`).
    *   **`Utils`**: Helpers for object graph manipulation, hydration, and reflection-based mapping.

## Tests

Run tests with:

```bash
dotnet test tests/Vrb.Tests/Vrb.Tests.csproj --logger "console;verbosity=detailed"
```

### Core Tests

| Test | File | Purpose |
|------|------|---------|
| `VrbRoundTrip_PreservesBinaryFidelity` | `BinaryFidelityTest.cs` | **Primary validation test.** Performs a complete VRB → JSON → VRB round-trip on `savegame.vrb` and verifies the output file is functionally identical (size within 1%, readable by engine). |
| `VrbRoundTrip_SessionComponents` | `BinaryFidelityTest.cs` | Tests round-trip for `sessioncomponents.vrb` files to verify non-savegame VRB types work correctly. |
| `SimplifiedDeveloperExperience_Test` | `ExampleUsageTests.cs` | Validates the README code examples work correctly. Ensures the simplified `Vrb.Initialize()` and `Vrb.Service` API functions as documented. |
| `CanConvertVrbToJson` | `IntegrationTests.cs` | Basic integration test that converts a VRB file to JSON and validates the output is parseable JSON containing expected types. |
| `CanConvertVrbToJsonWithValidation` | `IntegrationTests.cs` | Same as above but with validation enabled to test the validation code path. |

### CLI Tests

| Test | File | Purpose |
|------|------|---------|
| `CliRoundTrip_SaveGame_PreservesData` | `CliRoundTripTest.cs` | **End-to-end CLI test.** Invokes `Program.Main` directly to convert VRB → JSON → VRB, then compares file sizes and verifies the output is readable. |
| `CliValidation_SaveGame_Passes` | `CliRoundTripTest.cs` | Tests the single-argument validation mode and verifies it completes without errors. |

> **Note:** CLI tests invoke `Program.Main` directly via reflection rather than spawning a separate process, making them faster and easier to debug.

### Test Requirements

- **Space Engineers 2** must be installed (tests load game assemblies)
- A valid `savegame.vrb` file must exist in one of:
  - `%APPDATA%\SpaceEngineers2\AppData\SaveGames\<any save>\savegame.vrb`
  - `tests/Vrb.Tests/Tests/savegame.vrb` (local override)

## Disclaimer

This is an unofficial community tool. It is not affiliated with or endorsed by Keen Software House.
*   **Always backup your save files before editing.**
*   Modifying save files can corrupt your save or cause game instability.
*   Use at your own risk.
