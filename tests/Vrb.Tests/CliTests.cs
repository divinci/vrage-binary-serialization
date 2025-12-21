using System.Reflection;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Tests the VRB command-line interface by invoking Program.Main.
/// Tests the three core CLI operations:
/// <list type="number">
///   <item><description>VRB → JSON conversion (vrb.exe input.vrb output.json)</description></item>
///   <item><description>JSON → VRB conversion (vrb.exe input.json output.vrb)</description></item>
///   <item><description>Validation mode (vrb.exe input.vrb)</description></item>
/// </list>
/// </summary>
public class CliTests : IClassFixture<VrbTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly VrbTestFixture _fixture;

    public CliTests(VrbTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region VRB → JSON CLI Tests

    /// <summary>
    /// Tests CLI conversion from VRB to JSON.
    /// </summary>
    [Fact]
    public void Cli_VrbToJson_CreatesJsonFile()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping CLI test.");
            return;
        }

        var tempDir = CreateTempDirectory();
        var jsonPath = Path.Combine(tempDir, "savegame.json");

        try
        {
            _output.WriteLine($"Input: {vrbPath}");
            _output.WriteLine($"Output: {jsonPath}");

            // Act
            RunCliMain(vrbPath, jsonPath);

            // Assert
            Assert.True(File.Exists(jsonPath), "JSON file was not created");
            
            var jsonSize = new FileInfo(jsonPath).Length;
            _output.WriteLine($"Created JSON file: {jsonSize:N0} bytes");
            Assert.True(jsonSize > 0, "JSON file is empty");
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    #endregion

    #region JSON → VRB CLI Tests

    /// <summary>
    /// Tests CLI conversion from JSON to VRB.
    /// </summary>
    [Fact]
    public void Cli_JsonToVrb_CreatesVrbFile()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping CLI test.");
            return;
        }

        var tempDir = CreateTempDirectory();
        var jsonPath = Path.Combine(tempDir, "savegame.json");
        var outputVrbPath = Path.Combine(tempDir, "savegame.vrb");

        try
        {
            // First, create JSON via CLI
            RunCliMain(vrbPath, jsonPath);
            Assert.True(File.Exists(jsonPath), "JSON file was not created");

            // Act: Convert JSON back to VRB
            _output.WriteLine($"Converting JSON to VRB...");
            RunCliMain(jsonPath, outputVrbPath);

            // Assert
            Assert.True(File.Exists(outputVrbPath), "VRB file was not created");
            
            var vrbSize = new FileInfo(outputVrbPath).Length;
            _output.WriteLine($"Created VRB file: {vrbSize:N0} bytes");
            Assert.True(vrbSize > 0, "VRB file is empty");
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    #endregion

    #region Validation CLI Tests

    /// <summary>
    /// Tests CLI validation mode (single argument).
    /// </summary>
    [Fact]
    public void Cli_Validation_CompletesWithoutError()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping CLI test.");
            return;
        }

        _output.WriteLine($"Validating: {vrbPath}");

        // Act & Assert - single argument triggers validation mode
        var exception = Record.Exception(() => RunCliMain(vrbPath));

        if (exception != null)
        {
            _output.WriteLine($"Validation threw exception: {exception.Message}");
            Assert.Fail($"Validation mode failed: {exception.Message}");
        }

        _output.WriteLine("CLI validation completed without errors.");
    }

    #endregion

    #region Full Round-Trip CLI Test

    /// <summary>
    /// Tests a complete round-trip: VRB → JSON → VRB → JSON.
    /// Verifies the output VRB can be read back.
    /// </summary>
    [Fact]
    public void Cli_FullRoundTrip_DataPreserved()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping CLI test.");
            return;
        }

        var tempDir = CreateTempDirectory();
        var jsonPath1 = Path.Combine(tempDir, "step1.json");
        var vrbPath2 = Path.Combine(tempDir, "step2_savegame.vrb");
        var jsonPath3 = Path.Combine(tempDir, "step3.json");

        try
        {
            _output.WriteLine("Step 1: VRB → JSON");
            RunCliMain(vrbPath, jsonPath1);
            Assert.True(File.Exists(jsonPath1));
            var json1Size = new FileInfo(jsonPath1).Length;
            _output.WriteLine($"  JSON size: {json1Size:N0} bytes");

            _output.WriteLine("Step 2: JSON → VRB");
            RunCliMain(jsonPath1, vrbPath2);
            Assert.True(File.Exists(vrbPath2));
            _output.WriteLine($"  VRB size: {new FileInfo(vrbPath2).Length:N0} bytes");

            _output.WriteLine("Step 3: VRB → JSON (verify readable)");
            RunCliMain(vrbPath2, jsonPath3);
            Assert.True(File.Exists(jsonPath3));
            var json3Size = new FileInfo(jsonPath3).Length;
            _output.WriteLine($"  JSON size: {json3Size:N0} bytes");

            // JSON sizes should be identical (same data)
            Assert.Equal(json1Size, json3Size);
            _output.WriteLine("✓ Round-trip successful: JSON sizes match");
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Invokes the CLI Program.Main entry point via reflection.
    /// </summary>
    private void RunCliMain(params string[] args)
    {
        _output.WriteLine($"  > vrb.exe {string.Join(" ", args.Select(a => $"\"{a}\""))}");

        // Get the Vrb assembly (the CLI project)
        var vrbAssembly = typeof(global::Vrb.Options).Assembly;
        
        // Get the entry point (top-level statements create a synthetic Main)
        var entryPoint = vrbAssembly.EntryPoint 
            ?? throw new InvalidOperationException("Could not find entry point in Vrb assembly");

        // Determine parameter configuration
        var parameters = entryPoint.GetParameters();
        
        object?[] invokeArgs = parameters.Length switch
        {
            0 => Array.Empty<object?>(),
            1 when parameters[0].ParameterType == typeof(string[]) => new object?[] { args },
            _ => throw new InvalidOperationException($"Unexpected entry point signature: {entryPoint}")
        };

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
            throw ex.InnerException;
        }
    }

    /// <summary>
    /// Creates a unique temporary directory for test files.
    /// </summary>
    private static string CreateTempDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"vrb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Cleans up a temporary directory.
    /// </summary>
    private void CleanupTempDirectory(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
        catch (Exception ex)
        {
            _output.WriteLine($"Warning: Could not clean up temp directory: {ex.Message}");
        }
    }

    #endregion
}

