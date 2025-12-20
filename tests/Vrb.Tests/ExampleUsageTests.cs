using Vrb;
using Vrb.Core;
using System.IO;

namespace Vrb.Tests;

public class ExampleUsageTests
{
    [Fact]
    public void SimplifiedDeveloperExperience_Test()
    {
        // Setup: Find a real file to test with (standard SE2 save path)
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var vrbPath = Path.Combine(appData, "SpaceEngineers2", "AppData", "SaveGames", "Welcome To Verdure 2", "savegame.vrb");
        
        // Local override for CI/different environments
        var localTestFile = Path.Combine(Directory.GetCurrentDirectory(), "Tests", "savegame.vrb");
        if (File.Exists(localTestFile)) vrbPath = localTestFile;

        if (!File.Exists(vrbPath)) return;

        // --- START OF README EXAMPLE ---
        
        // 1. Initialize (once at startup)
        Vrb.Core.Vrb.Initialize();

        // 2. Convert VRB -> JSON
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame);

        // 3. Convert JSON -> VRB (pass the JSON string directly)
        var outputPath = Path.Combine(Path.GetTempPath(), "savegame_rehydrated_test.vrb");
        Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, TargetType.SaveGame);

        // --- END OF README EXAMPLE ---

        // Assertions to verify the example actually works
        Assert.NotNull(json);
        Assert.True(json.Length > 100);
        Assert.True(File.Exists(outputPath));

        // Cleanup
        if (File.Exists(outputPath)) File.Delete(outputPath);
    }
}
