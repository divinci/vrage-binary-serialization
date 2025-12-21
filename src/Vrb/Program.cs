using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using System.Text.Json;
using Vrb;
using Vrb.Core;
using Vrb.Utils;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddCommandLine(args)
    .Build();

// Configure Serilog
var logFileName = $"{DateTime.Now:yyyyMMdd-HHmmss}-vbr.log";
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.File(logFileName)
    .CreateLogger();

using var loggerFactory = new LoggerFactory().AddSerilog();
var logger = loggerFactory.CreateLogger<Program>();

// Parse Arguments
var result = Parser.Default.ParseArguments<Options>(args);

result.WithParsed(options =>
{
    var files = options.FilePaths.ToList();

    // Initialize VRB with Serilog
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

    if (files.Count == 0)
    {
        logger.LogError("No files provided. Usage: vrb.exe <input> [output]");
        return;
    }

    string inputPath = files[0];
    string? outputPath = files.Count > 1 ? files[1] : null;

    bool isVrbInput = inputPath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase);
    bool isJsonInput = inputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    if (files.Count == 1)
    {
        // 1 Argument: Validate Mode (must be .vrb)
        if (isVrbInput)
        {
            logger.LogInformation("Mode: Validation (Round-Trip) for {Input}", inputPath);
            
            var targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(inputPath));
            
            if (targetType == null)
            {
                logger.LogWarning("Could not determine file type from filename. Attempting to detect type by reading file...");
                
                foreach (var type in Enum.GetValues<TargetType>())
                {
                    try
                    {
                        // Try validating with this type
                        // Note: Validate mode performs deserialization AND re-serialization
                        processor.DeserializeVrb(inputPath, type, validate: true);
                        logger.LogInformation("Detection Successful: File is {Type}", type);
                        logger.LogInformation("Validation Passed: {Input}", inputPath);
                        return; // Exit after success
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
                // Standard path with known type
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
        else
        {
             logger.LogError("Single argument must be a .vrb file for validation.");
        }
    }
    else if (files.Count == 2 && outputPath != null)
    {
        bool isJsonOutput = outputPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        bool isVrbOutput = outputPath.EndsWith(".vrb", StringComparison.OrdinalIgnoreCase);

        if (isVrbInput && isJsonOutput)
        {
            // Mode: VRB -> JSON
            logger.LogInformation("Mode: Convert VRB to JSON ({Input} -> {Output})", inputPath, outputPath);
            
            var targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(inputPath));
            
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
                        // Continue
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
        else if (isJsonInput && isVrbOutput)
        {
             // Mode: JSON -> VRB
            logger.LogInformation("Mode: Convert JSON to VRB ({Input} -> {Output})", inputPath, outputPath);
            
            // Try to detect type from output filename first (e.g. savegame.vrb)
            var targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(outputPath));
            
            // If output name is generic, try input name
            if (targetType == null)
            {
                targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(inputPath));
            }

            // If still null, try to inspect the JSON content for $Type field
            if (targetType == null)
            {
                 logger.LogWarning("Filename detection failed. Inspecting JSON content for $Type...");
                 try 
                 {
                     var jsonText = File.ReadAllText(inputPath);
                     using var doc = JsonDocument.Parse(jsonText);
                     
                     if (doc.RootElement.TryGetProperty("$Type", out var typeProp))
                     {
                         var typeName = typeProp.GetString();
                         if (typeName != null)
                         {
                             if (typeName.Contains("EntityBundle")) targetType = TargetType.SaveGame;
                             else if (typeName.Contains("SessionComponentsSnapshot")) targetType = TargetType.SessionComponents;
                             else if (typeName.Contains("AssetJournal")) targetType = TargetType.AssetJournal;
                             
                             if (targetType != null)
                             {
                                 logger.LogInformation("Detected type from JSON $Type field: {Type}", targetType);
                             }
                         }
                     }
                 }
                 catch (Exception ex)
                 {
                     logger.LogWarning(ex, "Failed to inspect JSON content for type detection.");
                 }
            }

            if (targetType == null)
            {
                logger.LogError("Unknown file type. Ensure filename contains 'savegame', 'sessioncomponents', or 'assetjournal', or that the JSON content is valid.");
                return;
            }

            try
            {
                var jsonContent = File.ReadAllText(inputPath);
                processor.SerializeJsonToVrb(jsonContent, outputPath, targetType.Value);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Conversion failed.");
            }
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
    // CommandLineParser prints help automatically to Console/Error.
    // We just log that parsing failed.
    logger.LogWarning("Invalid arguments provided.");
});

// Helper to map filenames to types
static TargetType? GetTargetTypeFromFilename(string fileName)
{
    if (fileName.Contains("sessioncomponents", StringComparison.OrdinalIgnoreCase))
    {
        return TargetType.SessionComponents;
    }
    else if (fileName.Contains("savegame", StringComparison.OrdinalIgnoreCase))
    {
        return TargetType.SaveGame;
    }
    else if (fileName.Contains("assetjournal", StringComparison.OrdinalIgnoreCase))
    {
        return TargetType.AssetJournal;
    }
    return null;
}
