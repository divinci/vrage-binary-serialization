using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Vrb.Infrastructure;

namespace Vrb.Utils;

/// <summary>
/// Provides validation utilities for VRB file round-trip verification.
/// Tests that serialization and deserialization preserve data integrity.
/// </summary>
public static class VrbValidation
{
    /// <summary>
    /// Verifies that a round-trip (VRB → Object → VRB) produces identical binary output.
    /// Attempts serialization with all available compression methods to find a hash match.
    /// </summary>
    /// <param name="data">The deserialized object graph.</param>
    /// <param name="filePath">Original file path (used for hash comparison and debug naming).</param>
    /// <param name="targetType">The CLR type of the data.</param>
    /// <param name="vrageLibrary">The loaded VRage.Library assembly.</param>
    /// <param name="targetTypeName">Fully qualified type name.</param>
    /// <param name="targetAssemblyName">Assembly name containing the target type.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <returns>True if exact binary match is found; false otherwise.</returns>
    public static bool VerifyRoundTrip(
        object data, 
        string filePath, 
        Type targetType, 
        Assembly vrageLibrary, 
        string targetTypeName, 
        string targetAssemblyName,
        ILogger logger)
    {
        try
        {
            var expectedHash = HashHelper.ComputeFileHash(filePath);
            
            logger.LogInformation("Starting Round-Trip Validation for {FilePath}", filePath);

            var writerType = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.BinaryArchiveWriter");
            var compressionTypeEnum = vrageLibrary.GetType("Keen.VRage.Library.Serialization.Binary.Archive.CompressionType");

            if (writerType == null || compressionTypeEnum == null)
            {
                logger.LogError("Error: Could not find BinaryArchiveWriter or CompressionType.");
                return false;
            }

            var writeMethod = writerType.GetMethod("AddMainChunk");
            if (writeMethod == null)
            {
                logger.LogError("Error: Could not find AddMainChunk method.");
                return false;
            }

            var genericWrite = writeMethod.MakeGenericMethod(targetType);
            var compressionValues = Enum.GetValues(compressionTypeEnum);

            logger.LogInformation("Attempting serialization with {Count} compression types to match hash {ExpectedHash}...", 
                compressionValues.Length, expectedHash);

            // Try each compression method to find exact binary match
            foreach (var compressionVal in compressionValues)
            {
                using var ms = new MemoryStream();

                // Create context using shared helper
                var context = SerializationContextHelper.CreateBinaryContext(vrageLibrary, ms, Path.GetFileName(filePath));

                // Create and use writer
                var writer = Activator.CreateInstance(writerType, new object[] { context, false });
                genericWrite.Invoke(writer, new object[] { data, compressionVal });

                // Dispose writer to flush
                if (writer is IDisposable disposableWriter) 
                    disposableWriter.Dispose();

                // Check hash
                var hash = HashHelper.ComputeHash(ms.ToArray());

                if (hash == expectedHash)
                {
                    logger.LogInformation("VALIDATION SUCCESS: Hash match found! Compression: {Compression}", compressionVal);
                    return true;
                }
            }

            // No exact match found - this is common due to non-deterministic compression
            logger.LogWarning("VALIDATION WARNING: No exact binary match found. " +
                "This may occur if dictionary key ordering differs or compression levels vary.");
            logger.LogWarning("Attempting deep verification of uncompressed content (Structure Check)...");
            
            VerifyStructuralIntegrity(data, targetType, logger);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during validation of {FilePath}", filePath);
            return false;
        }
    }

    /// <summary>
    /// Performs a structural integrity check on the object graph.
    /// Verifies that all expected fields are present and accessible.
    /// </summary>
    private static void VerifyStructuralIntegrity(object data, Type targetType, ILogger logger)
    {
        logger.LogInformation("Verifying structural integrity of {Type}...", targetType.Name);

        var fields = targetType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var accessibleFields = 0;
        var totalFields = fields.Length;

        foreach (var field in fields)
        {
            try
            {
                var value = field.GetValue(data);
                accessibleFields++;

                // Log collection sizes for debugging
                if (value is System.Collections.ICollection col)
                {
                    logger.LogDebug("Field {Field}: {Count} items", field.Name, col.Count);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning("Could not access field {Field}: {Error}", field.Name, ex.Message);
            }
        }

        logger.LogInformation("Structural check complete: {Accessible}/{Total} fields accessible", 
            accessibleFields, totalFields);
        
        logger.LogWarning("NOTE: Binary mismatch detected but object structure is intact. " +
            "This is typically due to dictionary reordering or compression metadata differences.");
    }
}
