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

var services = new ServiceCollection();

// Logging
services.AddLogging(builder =>
{
    builder.ClearProviders();
    builder.AddSerilog(dispose: true);
});

// Services
services.AddVrbServices();

using var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

// Parse Arguments
var result = Parser.Default.ParseArguments<Options>(args);

result.WithParsed(options =>
{
    logger.LogInformation("VRB Tool Starting...");

    try
    {
        // Initialize VRB (Find Game, Load Assemblies, Init Environment)
        serviceProvider.InitializeVrb();
    }
    catch (DirectoryNotFoundException)
    {
        // Error is already logged by InitializeVrb
        return;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to initialize VRB environment.");
        return;
    }

    var processor = serviceProvider.GetRequiredService<VrbProcessingService>();

    if (!string.IsNullOrEmpty(options.ToJsonPath))
    {
        logger.LogInformation("Processing single file to JSON: {FilePath}", options.ToJsonPath);
        processor.ProcessFile(options.ToJsonPath, options.Validate);
    }
    else if (!string.IsNullOrEmpty(options.FromJsonPath))
    {
        logger.LogInformation("Processing JSON to VRB: {FilePath}", options.FromJsonPath);
        processor.ProcessFile(options.FromJsonPath, options.Validate);
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
