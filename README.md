# VRB Serialization Tool (Space Engineers 2)

**NuGet Package:** [`Vrb.Core`](https://www.nuget.org/packages/Vrb.Core) (Version matches SE2 Build, e.g., `21190635.0.0`)

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

The project is structured for modularity:

*   **`src/Vrb`**: The Console CLI application entry point.
*   **`src/Vrb.Core`**: The reusable Class Library containing all logic.
    *   **`Core`**: Core logic including `VrbProcessingService` (Orchestrator) and `GameEnvironmentInitializer`.
    *   **`Infrastructure`**: Handles external concerns like finding the game path (`GameInstallLocator`) and loading assemblies (`GameAssemblyManager`).
    *   **`Utils`**: Helpers for object graph manipulation, hydration, and reflection-based mapping.

## Disclaimer

This is an unofficial community tool. It is not affiliated with or endorsed by Keen Software House.
*   **Always backup your save files before editing.**
*   Modifying save files can corrupt your save or cause game instability.
*   Use at your own risk.
