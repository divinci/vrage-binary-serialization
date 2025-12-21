// VRB CLI Tool - Space Engineers 2 Binary Serialization Tool
// ============================================================
// Provides command-line access to VRB file operations:
//   - VRB → JSON conversion
//   - JSON → VRB conversion
//   - VRB validation (round-trip integrity check)
//
// Usage:
//   vrb.exe <input.vrb>                    - Validate mode
//   vrb.exe <input.vrb> <output.json>      - Convert VRB to JSON
//   vrb.exe <input.json> <output.vrb>      - Convert JSON to VRB

using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using Vrb;
using Vrb.Core;
using Vrb.Utils;

// ============================================================
// Configuration & Logging Setup
// ============================================================

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddCommandLine(args)
    .Build();

// Configure Serilog for file logging
var logFileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-vbr.log";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(logFileName)
    .CreateLogger();

using var loggerFactory = new LoggerFactory().AddSerilog();
var logger = loggerFactory.CreateLogger<Program>();

// ============================================================
// Command Line Parsing
// ============================================================

var result = Parser.Default.ParseArguments<Options>(args);

result.WithParsed(options =>
{
    var files = options.FilePaths.ToList();

    // Initialize the VRB library with Serilog
    try 
    {
        Vrb.Core.Vrb.Initialize(configureLogging: builder => 
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Failed to initialize VRB environment.");
        return;
    }

    logger.LogInformation("VRB Tool Starting...");
    
    var processor = Vrb.Core.Vrb.Service;
    if (processor == null)
    {
         logger.LogCritical("VRB Service failed to initialize.");
         return;
    }

    // Validate arguments
    if (files.Count == 0)
    {
        logger.LogError("No files provided. Usage: vrb.exe <input> [output]");
        return;
    }

    string inputPath = files[0];
    string? outputPath = files.Count > 1 ? files[1] : null;

    bool isVrbInput = inputPath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase);
    bool isJsonInput = inputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    // ============================================================
    // Operation Mode Selection
    // ============================================================

    if (files.Count == 1)
    {
        // Single argument: Validation mode (must be .vrb)
        HandleValidationMode(inputPath, isVrbInput, processor, logger);
    }
    else if (files.Count == 2 && outputPath != null)
    {
        // Two arguments: Conversion mode
        bool isJsonOutput = outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        bool isVrbOutput = outputPath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase);

        if (isVrbInput && isJsonOutput)
        {
            HandleVrbToJsonConversion(inputPath, outputPath, processor, logger);
        }
        else if (isJsonInput && isVrbOutput)
        {
            HandleJsonToVrbConversion(inputPath, outputPath, processor, logger);
        }
        else
        {
            logger.LogError("Invalid file extension combination. Use (.vrb .json) or (.json .vrb).");
        }
    }
    else
    {
        logger.LogError("Too many arguments. Expected 1 or 2 file paths.");
    }

    logger.LogInformation("Processing complete.");
});

result.WithNotParsed(errors =>
{
    logger.LogWarning("Invalid arguments provided.");
});

// ============================================================
// Operation Handlers
// ============================================================

/// <summary>
/// Handles validation mode: verifies VRB can be deserialized and re-serialized correctly.
/// </summary>
static void HandleValidationMode(string inputPath, bool isVrbInput, VrbProcessingService processor, Microsoft.Extensions.Logging.ILogger logger)
{
    if (!isVrbInput)
    {
        logger.LogError("Single argument must be a .vrb file for validation.");
        return;
    }

    logger.LogInformation("Mode: Validation (Round-Trip) for {Input}", inputPath);
    
    var targetType = TargetTypeHelper.GetFromFilename(Path.GetFileNameWithoutExtension(inputPath));
    
    if (targetType == null)
    {
        logger.LogWarning("Could not determine file type from filename. Attempting to detect type by reading file...");
        
        // Try each known type until one succeeds
        foreach (var type in Enum.GetValues<TargetType>())
        {
            try
            {
                processor.DeserializeVrb(inputPath, type, validate: true);
                logger.LogInformation("Detection Successful: File is {Type}", type);
                logger.LogInformation("Validation Passed: {Input}", inputPath);
                return;
            }
            catch
            {
                // Continue to next type
            }
        }
        
        logger.LogError("Validation Failed: Could not identify valid VRB type or file is corrupt.");
    }
    else
    {
        try 
        {
            processor.DeserializeVrb(inputPath, targetType.Value, validate: true);
            logger.LogInformation("Validation Passed: {Input}", inputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Validation Failed.");
        }
    }
}

/// <summary>
/// Handles VRB to JSON conversion.
/// </summary>
static void HandleVrbToJsonConversion(string inputPath, string outputPath, VrbProcessingService processor, Microsoft.Extensions.Logging.ILogger logger)
{
    logger.LogInformation("Mode: Convert VRB to JSON ({Input} -> {Output})", inputPath, outputPath);
    
    var targetType = TargetTypeHelper.GetFromFilename(Path.GetFileNameWithoutExtension(inputPath));
    
    if (targetType == null)
    {
        logger.LogWarning("Could not determine file type from filename. Attempting brute-force detection...");
        
        foreach (var type in Enum.GetValues<TargetType>())
        {
            try
            {
                var json = processor.DeserializeVrb(inputPath, type, validate: false);
                File.WriteAllText(outputPath, json);
                logger.LogInformation("Detection Successful: File is {Type}", type);
                logger.LogInformation("Created JSON file: {Output}", outputPath);
                return;
            }
            catch
            {
                // Continue to next type
            }
        }
        
        logger.LogError("Conversion Failed: Could not identify valid VRB type.");
    }
    else
    {
        try 
        {
            var json = processor.DeserializeVrb(inputPath, targetType.Value, validate: false);
            File.WriteAllText(outputPath, json);
            logger.LogInformation("Created JSON file: {Output}", outputPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Conversion failed.");
        }
    }
}

/// <summary>
/// Handles JSON to VRB conversion.
/// </summary>
static void HandleJsonToVrbConversion(string inputPath, string outputPath, VrbProcessingService processor, Microsoft.Extensions.Logging.ILogger logger)
{
    logger.LogInformation("Mode: Convert JSON to VRB ({Input} -> {Output})", inputPath, outputPath);
    
    // Try to detect type from output filename first
    var targetType = TargetTypeHelper.GetFromFilename(Path.GetFileNameWithoutExtension(outputPath));
    
    // If output name is generic, try input name
    if (targetType == null)
    {
        targetType = TargetTypeHelper.GetFromFilename(Path.GetFileNameWithoutExtension(inputPath));
    }

    // If still null, try to inspect the JSON content
    if (targetType == null)
    {
        logger.LogWarning("Filename detection failed. Inspecting JSON content for $Type...");
        try 
        {
            var jsonText = File.ReadAllText(inputPath);
            targetType = TargetTypeHelper.GetFromJsonContent(jsonText);
            
            if (targetType != null)
            {
                logger.LogInformation("Detected type from JSON $Type field: {Type}", targetType);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to inspect JSON content for type detection.");
        }
    }

    if (targetType == null)
    {
        logger.LogError("Unknown file type. Ensure filename contains 'savegame', 'sessioncomponents', or 'assetjournal', or that the JSON content has a valid $Type field.");
        return;
    }

    try
    {
        var jsonContent = File.ReadAllText(inputPath);
        processor.SerializeJsonToVrb(jsonContent, outputPath, targetType.Value);
        logger.LogInformation("Created VRB file: {Output}", outputPath);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Conversion failed.");
    }
}
