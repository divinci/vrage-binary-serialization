using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using vrb;
using vrb.Core;

namespace Vrb.Tests;

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
        var vrbPath = Path.Combine(appData, "SpaceEngineers2", "AppData", "SaveGames", "Welcome To Verdure 2", "savegame.vrb");

        // Allow for local test file override
        var localTestFile = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "savegame.vrb");
        if (File.Exists(localTestFile))
        {
            vrbPath = localTestFile;
        }

        if (!File.Exists(vrbPath))
        {
            // Skip test if file doesn't exist (e.g. on CI or different machine)
            Assert.Fail($"Test file not found at: {vrbPath}");
            return;
        }

        try
        {
            // Initialize VRB (Sleek!)
            // We configure logging to Console for debug
            vrb.Vrb.Initialize(configureLogging: builder => 
            {
                builder.ClearProviders();
                builder.AddConsole(); 
            });
        }
        catch (DirectoryNotFoundException)
        {
            Assert.Fail("Could not find Game Install Path. Cannot run integration test.");
        }

        // Act
        var output = vrb.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate);

        // Assert
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
