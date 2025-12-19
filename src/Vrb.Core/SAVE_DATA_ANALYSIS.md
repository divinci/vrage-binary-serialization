# Save Data Analysis

This document outlines the findings from analyzing the deserialized JSON save files for Space Engineers 2.

## Files Analyzed
- `sessioncomponents.vrb.json` (~289MB): Contains global session components, settings, and world state.
- `savegame.vrb.json` (~31MB): Contains entity data (blocks, characters, grids) and bundles.
- `assetjournal.vrb.json` (~700KB): Contains rendering and asset streaming data (mostly irrelevant for gameplay progress).

## Key Findings

### 1. Game Mode & Settings (`sessioncomponents.vrb.json`)
The `Keen.Game2.Simulation.GameSystems.Player.GameModeSessionComponent` controls the global game mode settings.
- **Location:** Found around line 523 in `sessioncomponents.vrb.json`.
- **Key Fields:**
  - `CreativeGlobal`: Currently `false`. Setting this to `true` could enable Creative Mode features (unlimited resources, instant building) for all players.
  - `CreativeOwners`: A list of player IDs who have creative mode enabled individually.

### 2. Player Data (`sessioncomponents.vrb.json`)
The `Keen.Game2.Simulation.GameSystems.Player.InProcessPerPlayerDataSessionComponent` stores specific data for each player identity.
- **Location:** Found around line 459 in `sessioncomponents.vrb.json`.
- **Structure:**
  - It contains a `PerPlayerData` dictionary, where keys are Player Identity IDs (e.g., `-1559595548`).
  - **Contents per Player:**
    - `Keen.Game2.Simulation.GameSystems.Control.ControlPerPlayerData`: Tracks the `LastControlledEntity`.
    - `Keen.Game2.Simulation.GameSystems.GPS.GPSMarkersServerData`: Contains a list of `GPSMarkers` (e.g., "Oblivara Station", "Helionis Station").
    - *Note:* This section was previously failing to serialize but is now fully visible after a tool fix.

### 3. Missions & Triggers (`sessioncomponents.vrb.json` & `savegame.vrb.json`)
Both files contain numerous references to mission triggers and conditions:
- `Keen.Game2.Simulation.GameSystems.Missions.Triggers.Conditions.PlayerHasItemConditionObjectBuilder`: Checks if a player has specific items.
- `Keen.Game2.Simulation.GameSystems.Missions.Triggers.Events.PlayerInventoryChangedEventObjectBuilder`: Events triggered by inventory changes.
- **Observation:** These seem to be part of the scenario logic (e.g., "UnfinishedResearchOutpost") rather than a persistent "Tech Tree". Modifying these might break specific quests or skip objectives.

## Technical Fixes Implemented
- **Serialization Error Resolved:** The tool's `DebugObjectConverter` was updated to robustly handle custom Dictionary implementations that yield `KeyValuePair` objects instead of `DictionaryEntry` objects. This fixed the crash when inspecting `PerPlayerData` and ensures all future dictionary-like structures are correctly processed.

## Recommendations for Modding

**To Enable Creative Mode:**
1. Open `sessioncomponents.vrb.json`.
2. Search for `"CreativeGlobal": false`.
3. Change it to `"CreativeGlobal": true`.
4. (Theory) This should unlock all building capabilities.

**To Modify GPS/Player State:**
- You can now edit `PerPlayerData` in `sessioncomponents.vrb.json` to add, remove, or modify GPS markers for specific players.
- You can potentially change the `LastControlledEntity` to spawn into a different body/ship (advanced).