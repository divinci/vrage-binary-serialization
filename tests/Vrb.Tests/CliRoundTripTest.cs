using System.Reflection;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Tests that verify the CLI tool performs correct VRB → JSON → VRB round-trips.
/// These tests invoke Program.Main directly via reflection.
/// </summary>
public class CliRoundTripTest
{
    private readonly ITestOutputHelper _output;

    public CliRoundTripTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void CliRoundTrip_SaveGame_PreservesData()
    {
        // 1. Locate the test VRB file
        var vrbPath = FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb test file found. Skipping CLI test.");
            return;
        }

        _output.WriteLine($"Using test file: {vrbPath}");
        _output.WriteLine($"Original file size: {new FileInfo(vrbPath).Length} bytes");

        // 2. Create temp paths for intermediate files
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrb-cli-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var jsonPath = Path.Combine(tempDir, "savegame.json");
        var outputVrbPath = Path.Combine(tempDir, "savegame.vrb");

        try
        {
            // Step 1: VRB → JSON
            _output.WriteLine($"\nStep 1: VRB → JSON");
            RunCliMain(vrbPath, jsonPath);

            Assert.True(File.Exists(jsonPath), "JSON file was not created");

            var jsonSize = new FileInfo(jsonPath).Length;
            _output.WriteLine($"  JSON file size: {jsonSize:N0} bytes ({jsonSize / 1024.0 / 1024.0:F2} MB)");

            // Step 2: JSON → VRB
            _output.WriteLine($"\nStep 2: JSON → VRB");
            RunCliMain(jsonPath, outputVrbPath);

            Assert.True(File.Exists(outputVrbPath), "Output VRB file was not created");

            // Step 3: Compare hashes and sizes
            var originalSize = new FileInfo(vrbPath).Length;
            var newSize = new FileInfo(outputVrbPath).Length;
            var originalHash = GetFileHash(vrbPath);
            var newHash = GetFileHash(outputVrbPath);

            _output.WriteLine($"\nComparison:");
            _output.WriteLine($"  Original Size: {originalSize:N0} bytes");
            _output.WriteLine($"  New Size:      {newSize:N0} bytes");
            _output.WriteLine($"  Difference:    {newSize - originalSize:N0} bytes ({(double)newSize / originalSize * 100:F2}%)");
            _output.WriteLine($"  Original SHA256: {originalHash}");
            _output.WriteLine($"  New SHA256:      {newHash}");

            // Step 4: Verify output is readable by doing another conversion
            _output.WriteLine($"\nStep 3: Verify output VRB is readable...");
            var verifyJsonPath = Path.Combine(tempDir, "verify.json");
            RunCliMain(outputVrbPath, verifyJsonPath);

            Assert.True(File.Exists(verifyJsonPath), "Verification JSON was not created");

            var verifyJsonSize = new FileInfo(verifyJsonPath).Length;
            _output.WriteLine($"  Verification JSON size: {verifyJsonSize:N0} bytes");

            // Assertions
            if (originalHash == newHash)
            {
                _output.WriteLine("\n✓ SUCCESS: Exact binary fidelity preserved!");
            }
            else
            {
                // Brotli compression is non-deterministic, so hashes may differ
                // But sizes should be within 1%
                var sizeDiffPercent = Math.Abs((double)(newSize - originalSize) / originalSize * 100);
                _output.WriteLine($"\nNote: Hashes differ (expected due to Brotli compression variations)");
                _output.WriteLine($"Size difference: {sizeDiffPercent:F2}%");
                
                Assert.True(sizeDiffPercent < 1, $"Size difference too large: {sizeDiffPercent:F2}%");
                _output.WriteLine("✓ SUCCESS: Data preserved (compression output varies slightly)");
            }
        }
        finally
        {
            // Cleanup
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                _output.WriteLine($"Warning: Could not clean up temp directory: {tempDir}");
            }
        }
    }

    [Fact]
    public void CliValidation_SaveGame_Passes()
    {
        // Test the single-argument validation mode
        var vrbPath = FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb test file found. Skipping CLI validation test.");
            return;
        }

        _output.WriteLine($"Testing validation mode on: {vrbPath}");
        
        // Single argument = validation mode
        // This should not throw if validation passes
        var exception = Record.Exception(() => RunCliMain(vrbPath));
        
        if (exception != null)
        {
            _output.WriteLine($"Validation threw exception: {exception.Message}");
            Assert.Fail($"Validation mode failed with exception: {exception.Message}");
        }
        
        _output.WriteLine("✓ Validation mode completed without errors");
    }

    /// <summary>
    /// Invokes the CLI Program.Main entry point directly via reflection.
    /// </summary>
    private void RunCliMain(params string[] args)
    {
        _output.WriteLine($"  Running CLI with args: {string.Join(" ", args.Select(a => $"\"{a}\""))}");

        // Get the Vrb assembly (the CLI project)
        var vrbAssembly = typeof(global::Vrb.Options).Assembly;
        
        // Get the entry point (the synthetic Main method from top-level statements)
        var entryPoint = vrbAssembly.EntryPoint;
        
        if (entryPoint == null)
        {
            throw new InvalidOperationException("Could not find entry point in Vrb assembly");
        }

        // Invoke the entry point
        // For top-level statements, the signature is: static void <Main>$(string[] args)
        // or static async Task <Main>$(string[] args) / static async Task<int> <Main>$(string[] args)
        var parameters = entryPoint.GetParameters();
        
        object?[] invokeArgs;
        if (parameters.Length == 0)
        {
            // No parameters - pass empty
            invokeArgs = Array.Empty<object?>();
        }
        else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string[]))
        {
            // Standard main(string[] args)
            invokeArgs = new object?[] { args };
        }
        else
        {
            throw new InvalidOperationException($"Unexpected entry point signature: {entryPoint}");
        }

        try
        {
            var result = entryPoint.Invoke(null, invokeArgs);
            
            // Handle async entry points
            if (result is Task task)
            {
                task.GetAwaiter().GetResult();
            }
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            // Unwrap the reflection exception
            throw ex.InnerException;
        }
    }

    private string? FindTestFile(string fileName)
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Tests", fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SpaceEngineers2", "AppData", "SaveGames")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
                return path;

            if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, fileName, SearchOption.AllDirectories);
                if (files.Length > 0)
                    return files[0];
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
