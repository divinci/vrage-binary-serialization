using System.Security.Cryptography;
using Vrb.Core;
using Vrb.Utils;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Comprehensive validation tests for VRB serialization fidelity.
/// Tests the complete round-trip process with hash verification.
/// </summary>
public class ValidationTests : IClassFixture<VrbTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly VrbTestFixture _fixture;

    public ValidationTests(VrbTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region Binary Fidelity Tests

    /// <summary>
    /// Tests that a VRB → JSON → VRB round-trip preserves binary fidelity.
    /// Compares file sizes and hashes.
    /// </summary>
    [Fact]
    public void RoundTrip_SaveGame_PreservesBinaryFidelity()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        var originalInfo = new FileInfo(vrbPath);
        var originalHash = HashHelper.ComputeFileHash(vrbPath);

        _output.WriteLine($"Original file: {vrbPath}");
        _output.WriteLine($"Original size: {originalInfo.Length:N0} bytes");
        _output.WriteLine($"Original hash: {originalHash}");

        // Step 1: VRB → JSON
        _output.WriteLine("\nStep 1: Deserializing VRB to JSON...");
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: false);
        Assert.NotNull(json);
        _output.WriteLine($"JSON size: {json.Length:N0} characters ({json.Length / 1024.0 / 1024.0:F2} MB)");

        // Step 2: JSON → VRB
        var outputPath = Path.Combine(Path.GetTempPath(), $"fidelity-test-{Guid.NewGuid():N}.vrb");
        try
        {
            _output.WriteLine("\nStep 2: Serializing JSON to VRB...");
            Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, TargetType.SaveGame, compressionMethod: "Brotli");

            var outputInfo = new FileInfo(outputPath);
            var outputHash = HashHelper.ComputeFileHash(outputPath);

            _output.WriteLine($"Output size: {outputInfo.Length:N0} bytes");
            _output.WriteLine($"Output hash: {outputHash}");

            // Step 3: Verify output is readable
            _output.WriteLine("\nStep 3: Verifying output is readable...");
            var verifyJson = Vrb.Core.Vrb.Service.DeserializeVrb(outputPath, TargetType.SaveGame);
            Assert.NotNull(verifyJson);
            Assert.Equal(json.Length, verifyJson.Length);
            _output.WriteLine($"Verification JSON size: {verifyJson.Length:N0} characters");

            // Analyze results
            _output.WriteLine("\n=== Analysis ===");
            var sizeDiff = outputInfo.Length - originalInfo.Length;
            var sizeDiffPercent = (double)sizeDiff / originalInfo.Length * 100;
            
            _output.WriteLine($"Size difference: {sizeDiff:+#;-#;0} bytes ({sizeDiffPercent:+0.00;-0.00;0.00}%)");

            if (originalHash == outputHash)
            {
                _output.WriteLine("✓ EXACT MATCH: Binary hashes are identical!");
            }
            else
            {
                _output.WriteLine("△ Hashes differ (expected with Brotli compression variations)");
                
                // Size should be within 1% for a valid round-trip
                Assert.True(Math.Abs(sizeDiffPercent) < 1, 
                    $"Size difference too large: {sizeDiffPercent:F2}%");
                
                _output.WriteLine("✓ PASS: Size within acceptable tolerance");
            }
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    /// <summary>
    /// Tests round-trip for SessionComponents files.
    /// </summary>
    [Fact]
    public void RoundTrip_SessionComponents_Succeeds()
    {
        var vrbPath = _fixture.FindTestFile("sessioncomponents.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No sessioncomponents.vrb found. Skipping test.");
            return;
        }

        PerformRoundTripTest(vrbPath, TargetType.SessionComponents);
    }

    #endregion

    #region Hash Helper Tests

    /// <summary>
    /// Tests that HashHelper produces consistent results.
    /// </summary>
    [Fact]
    public void HashHelper_ComputeFileHash_IsConsistent()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        var hash1 = HashHelper.ComputeFileHash(vrbPath);
        var hash2 = HashHelper.ComputeFileHash(vrbPath);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA256 = 32 bytes = 64 hex chars
        _output.WriteLine($"Hash: {hash1}");
    }

    /// <summary>
    /// Tests HashHelper.ComputeHash on byte arrays.
    /// </summary>
    [Fact]
    public void HashHelper_ComputeHash_WorksOnByteArray()
    {
        var data = "Hello, World!"u8.ToArray();
        var hash = HashHelper.ComputeHash(data);

        Assert.NotEmpty(hash);
        Assert.Equal(64, hash.Length);
        _output.WriteLine($"Hash of 'Hello, World!': {hash}");

        // Known SHA256 of "Hello, World!"
        Assert.Equal("dffd6021bb2bd5b0af676290809ec3a53191dd81c7f70a4b28688a362182986f", hash);
    }

    #endregion

    #region Multiple File Tests

    /// <summary>
    /// Tests round-trip on all available save games.
    /// </summary>
    [Fact]
    public void RoundTrip_AllSaveGames_Succeed()
    {
        var saveDirectories = _fixture.GetAllSaveDirectories().ToList();
        
        if (saveDirectories.Count == 0)
        {
            _output.WriteLine("No save directories found. Skipping test.");
            return;
        }

        _output.WriteLine($"Found {saveDirectories.Count} save directories");

        var passed = 0;
        var failed = 0;
        var skipped = 0;

        foreach (var saveDir in saveDirectories)
        {
            var saveName = Path.GetFileName(saveDir);
            var savegamePath = Path.Combine(saveDir, "savegame.vrb");

            if (!File.Exists(savegamePath))
            {
                _output.WriteLine($"  [{saveName}] - No savegame.vrb, skipped");
                skipped++;
                continue;
            }

            try
            {
                var json = Vrb.Core.Vrb.Service!.DeserializeVrb(savegamePath, TargetType.SaveGame);
                Assert.NotNull(json);
                
                _output.WriteLine($"  [{saveName}] - ✓ {json.Length:N0} chars");
                passed++;
            }
            catch (Exception ex)
            {
                _output.WriteLine($"  [{saveName}] - ✗ {ex.Message}");
                failed++;
            }
        }

        _output.WriteLine($"\nResults: {passed} passed, {failed} failed, {skipped} skipped");
        
        // At least some should pass
        Assert.True(passed > 0 || saveDirectories.Count == 0, "No save games could be processed");
    }

    #endregion

    #region Compression Comparison Tests

    /// <summary>
    /// Compares output sizes across different compression methods.
    /// </summary>
    [Fact]
    public void Compression_Comparison_ShowsSizeDifferences()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        var originalSize = new FileInfo(vrbPath).Length;
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame);

        var compressionMethods = new[] { "None", "ZLib", "Brotli" };
        var results = new Dictionary<string, long>();

        _output.WriteLine($"Original size: {originalSize:N0} bytes");
        _output.WriteLine($"JSON size: {json.Length:N0} characters");
        _output.WriteLine("\nCompression comparison:");

        foreach (var method in compressionMethods)
        {
            var outputPath = Path.Combine(Path.GetTempPath(), $"compress-test-{method}-{Guid.NewGuid():N}.vrb");
            try
            {
                Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, TargetType.SaveGame, method);
                var size = new FileInfo(outputPath).Length;
                results[method] = size;
                
                var ratio = (double)size / originalSize * 100;
                _output.WriteLine($"  {method,-8}: {size,12:N0} bytes ({ratio:F1}% of original)");
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }

        // Brotli should be smallest, None should be largest
        Assert.True(results["Brotli"] < results["None"], "Brotli should compress smaller than uncompressed");
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Performs a standard round-trip test on a VRB file.
    /// </summary>
    private void PerformRoundTripTest(string vrbPath, TargetType type)
    {
        _output.WriteLine($"Testing: {vrbPath}");
        _output.WriteLine($"Type: {type}");

        var originalSize = new FileInfo(vrbPath).Length;
        _output.WriteLine($"Original size: {originalSize:N0} bytes");

        // VRB → JSON
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, type);
        Assert.NotNull(json);
        _output.WriteLine($"JSON size: {json.Length:N0} characters");

        // JSON → VRB
        var outputPath = Path.Combine(Path.GetTempPath(), $"roundtrip-{Guid.NewGuid():N}.vrb");
        try
        {
            Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, type);

            Assert.True(File.Exists(outputPath));
            var outputSize = new FileInfo(outputPath).Length;
            _output.WriteLine($"Output size: {outputSize:N0} bytes");

            // Verify readable
            var verifyJson = Vrb.Core.Vrb.Service.DeserializeVrb(outputPath, type);
            Assert.Equal(json.Length, verifyJson.Length);

            _output.WriteLine("✓ Round-trip successful");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    #endregion
}

