# VRB Serialization Tool (Space Engineers 2)

A powerful modding utility for **Space Engineers 2** that allows you to serialize and deserialize binary save files (`.vrb`) to and from human-readable JSON. This enables players and modders to edit save data, inspect internal game structures, and create external tools that interact with SE2 save files.

The tool leverages the game's own assemblies (`VRage.Library`, `Game2.Simulation`, etc.) to ensuring accurate data processing and full compatibility with the game's binary format.

**Current Platform Support:** Windows Only (due to game assembly dependencies).

## Features

- **Bidirectional Conversion:**
  - **VRB to JSON:** Convert binary save files to structured JSON for easy editing in any text editor.
  - **JSON to VRB:** Rehydrate your modified JSON back into a valid `.vrb` file that the game can load.
- **Full-Cycle Validation:** The `--validate` flag performs a lossless round-trip check (`VRB -> JSON -> VRB`) to ensure your data is preserved byte-for-byte.
- **Auto-Detection:** Automatically locates the Space Engineers 2 installation path via Steam registry keys or library folders.
- **Library Mode:** Can be referenced by other C#/.NET 9 applications to add VRB support to your own tools.
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

3.  **Locate the executable:**
    The built executable will be in `src/bin/Release/net9.0-windows/vrb.exe`.

## CLI Usage

The Command Line Interface (CLI) is the primary way to use the tool.

### 1. Convert VRB to JSON
Converts a binary save file to JSON. The output is printed to the console (STDOUT), so you should redirect it to a file.

```bash
# Syntax: vrb.exe --toJson <path-to-vrb> > <output-json>
vrb.exe --toJson "C:\Users\You\AppData\Roaming\SpaceEngineers2\SaveGames\MySave\savegame.vrb" > savegame.json
```

### 2. Convert JSON to VRB
Converts a JSON file back to a binary `.vrb` file. The output file will be created in the same directory as the input JSON, replacing the original extension (e.g., `savegame.json` -> `savegame.vrb`).

```bash
# Syntax: vrb.exe --fromJson <path-to-json>
vrb.exe --fromJson "savegame.json"
```

### 3. Validate Conversion (Lossless Check)
Verifies that the file can be converted to JSON and back to binary without *any* data loss. It compares the binary hash of the re-created file against the original.

```bash
vrb.exe --toJson "savegame.vrb" --validate
```

## Library Usage (C# / .NET)

You can use the core logic in your own .NET 9 applications.

### 1. Add Reference
Add a reference to the `vrb` project or DLL.

### 2. Initialize & Use
Use the provided extension methods to register services and initialize the environment (which loads the game assemblies).

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using vrb;
using vrb.Core;

// 1. Setup DI Container
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());

// 2. Register VRB Services
services.AddVrbServices();

var provider = services.BuildServiceProvider();

// 3. Initialize VRB Environment (Locates Game & Loads Assemblies)
try 
{
    provider.InitializeVrb(); // Can pass explicit path: InitializeVrb("C:/GamePath")
}
catch (DirectoryNotFoundException)
{
    Console.WriteLine("Could not find Space Engineers 2 installation!");
    return;
}

// 4. Use the Processing Service
var processor = provider.GetRequiredService<VrbProcessingService>();

// Example: Deserialize to JSON string (via redirect logic or custom handling)
// Note: The current ProcessFile method writes to Console, custom integration 
// might require adapting VrbProcessingService or using its internal logic.
processor.ProcessFile("path/to/savegame.vrb", validate: false);
```

## Architecture

The project is structured for modularity and ease of maintenance:

*   **`src/Core`**: Core logic including `VrbProcessingService` (Orchestrator) and `GameEnvironmentInitializer`.
*   **`src/Infrastructure`**: Handles external concerns like finding the game path (`GameInstallLocator`) and loading assemblies (`GameAssemblyManager`).
*   **`src/Utils`**: Helpers for object graph manipulation, hydration, and reflection-based mapping.
*   **`src/VrbSerializationSetup.cs`**: Extension methods for easy DI integration.

## Disclaimer

This is an unofficial community tool. It is not affiliated with or endorsed by Keen Software House.
*   **Always backup your save files before editing.**
*   Modifying save files can corrupt your save or cause game instability.
*   Use at your own risk.
