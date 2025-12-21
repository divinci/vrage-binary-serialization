using Vrb.Core;

namespace Vrb.Utils;

/// <summary>
/// Helper for detecting and working with VRB target types.
/// Provides utilities for determining the type of VRB file from filenames or JSON content.
/// </summary>
public static class TargetTypeHelper
{
    /// <summary>
    /// Attempts to determine the TargetType from a filename.
    /// Matches common naming patterns used in Space Engineers 2 save files.
    /// </summary>
    /// <param name="fileName">The filename (without extension) to analyze.</param>
    /// <returns>The detected TargetType, or null if the type cannot be determined.</returns>
    /// <example>
    /// GetFromFilename("savegame") => TargetType.SaveGame
    /// GetFromFilename("sessioncomponents") => TargetType.SessionComponents
    /// GetFromFilename("assetjournal") => TargetType.AssetJournal
    /// </example>
    public static TargetType? GetFromFilename(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        // Check for known patterns (case-insensitive)
        if (fileName.Contains("sessioncomponents", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.SessionComponents;
        }
        
        if (fileName.Contains("savegame", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.SaveGame;
        }
        
        if (fileName.Contains("assetjournal", StringComparison.OrdinalIgnoreCase))
        {
            return TargetType.AssetJournal;
        }

        return null;
    }

    /// <summary>
    /// Attempts to determine the TargetType from JSON content by inspecting the $Type field.
    /// </summary>
    /// <param name="json">The JSON content to analyze.</param>
    /// <returns>The detected TargetType, or null if the type cannot be determined.</returns>
    public static TargetType? GetFromJsonContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("$Type", out var typeProp))
            {
                var typeName = typeProp.GetString();
                if (typeName != null)
                {
                    if (typeName.Contains("EntityBundle"))
                        return TargetType.SaveGame;
                    if (typeName.Contains("SessionComponentsSnapshot"))
                        return TargetType.SessionComponents;
                    if (typeName.Contains("AssetJournal"))
                        return TargetType.AssetJournal;
                }
            }
        }
        catch
        {
            // JSON parsing failed, return null
        }

        return null;
    }

    /// <summary>
    /// Gets the fully qualified type name and assembly name for a given TargetType.
    /// </summary>
    /// <param name="type">The target type.</param>
    /// <returns>A tuple containing (TypeName, AssemblyName).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown for unknown target types.</exception>
    public static (string TypeName, string AssemblyName) GetTypeInfo(TargetType type)
    {
        return type switch
        {
            TargetType.SaveGame => ("Keen.VRage.Core.Game.Systems.EntityBundle", "VRage.Core.Game"),
            TargetType.SessionComponents => ("Keen.Game2.Simulation.RuntimeSystems.Saves.SessionComponentsSnapshot", "Game2.Simulation"),
            TargetType.AssetJournal => ("Keen.Game2.Game.EngineComponents.AssetJournal", "Game2.Game"),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown target type")
        };
    }
}

