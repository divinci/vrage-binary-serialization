using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using vrb;
using vrb.Core;

namespace vrbTests;

public class IntegrationTests
{
    [Fact]
    public void CanConvertVrbToJson()
    {
        RunTest(validate: false);
    }

    [Fact]
    public void CanConvertVrbToJsonWithValidation()
    {
        RunTest(validate: true);
    }

    private void RunTest(bool validate)
    {
        // Arrange
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var vrbPath = Path.Combine(appData, "SpaceEngineers2", "AppData", "SaveGames", "Welcome To Verdure 1", "savegame.vrb");

        if (!File.Exists(vrbPath))
        {
            // Skip test if file doesn't exist (e.g. on CI or different machine)
            // Assert.Fail($"Test file not found at: {vrbPath}");
            return;
        }

        var services = new ServiceCollection();
        // Use a logger that doesn't pollute Console.Out
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole(); // Enable logging to debug failures
        });

        services.AddVrbServices();

        var provider = services.BuildServiceProvider();
        
        try
        {
            // Initialize Environment (loads assemblies etc)
            provider.InitializeVrb();
        }
        catch (DirectoryNotFoundException)
        {
            Assert.Fail("Could not find Game Install Path. Cannot run integration test.");
        }

        var processor = provider.GetRequiredService<VrbProcessingService>();

        // Capture Console Output
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            // Act
            processor.ProcessFile(vrbPath, validate);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        // Assert
        var output = sw.ToString().Trim();
        Assert.False(string.IsNullOrWhiteSpace(output), "Output should not be empty");

        // Basic JSON validation
        try
        {
            using var doc = JsonDocument.Parse(output);
            Assert.NotNull(doc);

            // Optional: Check for a known property to verify it's the right object
            // For savegame.vrb -> Keen.VRage.Core.Game.Systems.EntityBundle
            var root = doc.RootElement;
            if (root.TryGetProperty("$type", out var typeProp))
            {
                var typeName = typeProp.GetString();
                Assert.Contains("EntityBundle", typeName ?? "");
            }
        }
        catch (JsonException ex)
        {
            Assert.Fail($"Output was not valid JSON: {ex.Message}\nOutput Preview: {output}");
        }
    }
}
