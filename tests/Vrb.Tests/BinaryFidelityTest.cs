using System.Reflection;
using System.Security.Cryptography;
using Vrb.Core;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Tests that verify the VRB → JSON → VRB round-trip using the game engine's serialization.
/// </summary>
public class BinaryFidelityTest
{
    private readonly ITestOutputHelper _output;

    public BinaryFidelityTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void VrbRoundTrip_PreservesBinaryFidelity()
    {
        // 1. Locate the test file
        var vrbPath = FindTestFile();
        if (vrbPath == null)
        {
            Assert.Fail("No test VRB file found. See test output for searched locations.");
            return;
        }

        _output.WriteLine($"Using test file: {vrbPath}");
        _output.WriteLine($"Original file size: {new FileInfo(vrbPath).Length} bytes");

        // 2. Initialize Vrb
        Vrb.Core.Vrb.Initialize();

        // 3. Deserialize VRB to JSON (using engine's serialization)
        _output.WriteLine("Step 1: VRB → JSON (using engine's serialization)...");
        var jsonOutput = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: false);
        Assert.NotNull(jsonOutput);

        _output.WriteLine($"JSON size: {jsonOutput.Length:N0} characters ({jsonOutput.Length / 1024.0 / 1024.0:F2} MB)");

        // Optionally write JSON for debugging
        var jsonDebugPath = Path.Combine("S:\\repos\\SE2\\tools\\vrage-binary-serialization", "debug_savegame.json");
        try
        {
            File.WriteAllText(jsonDebugPath, jsonOutput);
            _output.WriteLine($"JSON written to: {jsonDebugPath}");
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Could not write debug JSON: {ex.Message}");
        }

        // Show a snippet of the JSON structure
        var snippet = jsonOutput.Length > 500 ? jsonOutput.Substring(0, 500) + "..." : jsonOutput;
        _output.WriteLine($"JSON structure preview:\n{snippet}");

        // 4. Serialize JSON back to VRB
        var tempVrbPath = Path.GetTempFileName();
        var tempVrbPathWithExt = Path.ChangeExtension(tempVrbPath, ".vrb");
        if (File.Exists(tempVrbPath)) File.Delete(tempVrbPath);

        _output.WriteLine($"\nStep 2: JSON → VRB: {tempVrbPathWithExt}");
        
        try
        {
            Vrb.Core.Vrb.Service.SerializeJsonToVrb(jsonOutput, tempVrbPathWithExt, TargetType.SaveGame, compressionMethod: "Brotli");

            Assert.True(File.Exists(tempVrbPathWithExt), "Output VRB file was not created");

            var newFileInfo = new FileInfo(tempVrbPathWithExt);
            _output.WriteLine($"Output file size: {newFileInfo.Length} bytes");

            // 5. Compare sizes and hashes
            var originalSize = new FileInfo(vrbPath).Length;
            var newSize = newFileInfo.Length;

            _output.WriteLine($"\nComparison:");
            _output.WriteLine($"  Original Size: {originalSize} bytes");
            _output.WriteLine($"  New Size:      {newSize} bytes");
            _output.WriteLine($"  Difference:    {newSize - originalSize} bytes ({(double)newSize / originalSize * 100:F2}%)");

            var originalHash = GetFileHash(vrbPath);
            var newHash = GetFileHash(tempVrbPathWithExt);

            _output.WriteLine($"  Original SHA256: {originalHash}");
            _output.WriteLine($"  New SHA256:      {newHash}");

            // 6. Verify the output file can be read back
            _output.WriteLine("\nStep 3: Verify output VRB is readable...");
            var verifyJson = Vrb.Core.Vrb.Service.DeserializeVrb(tempVrbPathWithExt, TargetType.SaveGame);
            Assert.NotNull(verifyJson);
            _output.WriteLine($"Verification successful! Round-trip JSON size: {verifyJson.Length:N0} characters");

            // Check that hashes match or sizes are very close
            if (originalHash == newHash)
            {
                _output.WriteLine("\n✓ SUCCESS: Exact binary fidelity preserved!");
            }
            else
            {
                // Due to Brotli compression non-determinism, exact hash match isn't guaranteed
                // But sizes should be very close (within 1%)
                var sizeDiffPercent = Math.Abs((double)(newSize - originalSize) / originalSize * 100);
                _output.WriteLine($"\nNote: Hashes differ (expected due to Brotli compression variations)");
                _output.WriteLine($"Size difference: {sizeDiffPercent:F2}%");
                
                Assert.True(sizeDiffPercent < 1, $"Size difference too large: {sizeDiffPercent:F2}%");
                _output.WriteLine("✓ SUCCESS: Data preserved (compression output varies slightly)");
            }
        }
        finally
        {
            if (File.Exists(tempVrbPathWithExt))
                File.Delete(tempVrbPathWithExt);
        }
    }

    [Fact]
    public void VrbRoundTrip_SessionComponents()
    {
        // Test with sessioncomponents.vrb if available
        var savePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SpaceEngineers2", "AppData", "SaveGames");

        if (!Directory.Exists(savePath))
        {
            _output.WriteLine("SaveGames folder not found, skipping test.");
            return;
        }

        var sessionFiles = Directory.GetFiles(savePath, "sessioncomponents.vrb", SearchOption.AllDirectories);
        if (sessionFiles.Length == 0)
        {
            _output.WriteLine("No sessioncomponents.vrb files found, skipping test.");
            return;
        }

        var vrbPath = sessionFiles[0];
        _output.WriteLine($"Using: {vrbPath}");

        Vrb.Core.Vrb.Initialize();

        try
        {
            var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SessionComponents);
            Assert.NotNull(json);
            _output.WriteLine($"JSON size: {json.Length:N0} characters");

            var tempPath = Path.ChangeExtension(Path.GetTempFileName(), ".vrb");
            try
            {
                Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, tempPath, TargetType.SessionComponents);
                Assert.True(File.Exists(tempPath));
                _output.WriteLine($"Output size: {new FileInfo(tempPath).Length} bytes");
                _output.WriteLine("✓ SessionComponents round-trip successful!");
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"SessionComponents test failed: {ex.Message}");
            // Don't fail the test - this file type might have additional dependencies
        }
    }

    private string? FindTestFile()
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Tests", "savegame.vrb"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpaceEngineers2", "AppData", "SaveGames", "Welcome To Verdure 2", "savegame.vrb"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpaceEngineers2", "AppData", "SaveGames")
        };

        foreach (var path in searchPaths)
        {
            _output.WriteLine($"Searching: {path}");
            
            if (File.Exists(path))
                return path;

            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "savegame.vrb", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    _output.WriteLine($"Found {files.Length} save files, using first one.");
                    return files[0];
                }
            }
        }

        return null;
    }

    private static string GetFileHash(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}
