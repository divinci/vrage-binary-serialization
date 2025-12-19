using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using System.IO;
using vrb;
using vrb.Core;
using vrb.Infrastructure;
using vrb.Utils;

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
    // Initialize VRB with Serilog
    try 
    {
        vrb.Vrb.Initialize(configureLogging: builder => 
        {
            builder.ClearProviders();
            builder.AddSerilog(dispose: true);
        });
    }
    catch (Exception ex)
    {
        // Fallback logging if init fails (though Initialize mostly handles logging setup, if it throws, we might not have a logger yet from the provider)
        // But we have Serilog static logger configured above.
        Log.Error(ex, "Failed to initialize VRB environment.");
        return;
    }

    logger.LogInformation("VRB Tool Starting...");
    
    var processor = vrb.Vrb.Service;
    if (processor == null)
    {
         logger.LogCritical("VRB Service failed to initialize.");
         return;
    }

    if (!string.IsNullOrEmpty(options.ToJsonPath))
    {
        logger.LogInformation("Processing single file to JSON: {FilePath}", options.ToJsonPath);
        
        var targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(options.ToJsonPath));
        if (targetType == null)
        {
            logger.LogError("Unknown file type for {FileName}. Supported: savegame, sessioncomponents, assetjournal.", Path.GetFileName(options.ToJsonPath));
            return;
        }

        try 
        {
            var json = processor.DeserializeVrb(options.ToJsonPath, targetType.Value, options.Validate);
            Console.WriteLine(json);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deserialize VRB file.");
        }
    }
    else if (!string.IsNullOrEmpty(options.FromJsonPath))
    {
        logger.LogInformation("Processing JSON to VRB: {FilePath}", options.FromJsonPath);
        
        // Infer output name: file.vrb.json -> file.vrb, or file.json -> file.vrb
        var fileName = Path.GetFileName(options.FromJsonPath);
        string vrbName = fileName.EndsWith(".vrb.json", StringComparison.OrdinalIgnoreCase) 
            ? fileName.Replace(".vrb.json", ".vrb") 
            : fileName.Replace(".json", ".vrb"); 
            
        var vrbPath = Path.Combine(Path.GetDirectoryName(options.FromJsonPath) ?? "", vrbName);

        var targetType = GetTargetTypeFromFilename(Path.GetFileNameWithoutExtension(vrbName));
        if (targetType == null)
        {
            logger.LogError("Unknown file type for {FileName}. Supported: savegame, sessioncomponents, assetjournal.", vrbName);
            return;
        }

        try
        {
            var jsonContent = File.ReadAllText(options.FromJsonPath);
            processor.SerializeJsonToVrb(jsonContent, vrbPath, targetType.Value);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to serialize JSON to VRB.");
        }
    }
    else
    {
        logger.LogError("Please provide arguments. Use --help for usage.");
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
