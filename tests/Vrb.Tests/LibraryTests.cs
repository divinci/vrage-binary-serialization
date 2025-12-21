using System.Text.Json;
using Vrb.Core;
using Vrb.Utils;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Tests the Vrb.Core library API directly.
/// Tests the three core functions:
/// <list type="number">
///   <item><description>VRB → JSON (DeserializeVrb)</description></item>
///   <item><description>JSON → VRB (SerializeJsonToVrb)</description></item>
///   <item><description>Validation (DeserializeVrb with validate: true)</description></item>
/// </list>
/// </summary>
public class LibraryTests : IClassFixture<VrbTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly VrbTestFixture _fixture;

    public LibraryTests(VrbTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    #region VRB → JSON Tests

    /// <summary>
    /// Tests that a SaveGame VRB file can be deserialized to valid JSON.
    /// </summary>
    [Fact]
    public void VrbToJson_SaveGame_ReturnsValidJson()
    {
        // Arrange
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        _output.WriteLine($"Testing: {vrbPath}");

        // Act
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: false);

        // Assert
        Assert.NotNull(json);
        Assert.NotEmpty(json);
        _output.WriteLine($"JSON size: {json.Length:N0} characters");

        // Verify it's valid JSON with expected structure
        using var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
        
        if (doc.RootElement.TryGetProperty("$Type", out var typeProp))
        {
            var typeName = typeProp.GetString();
            Assert.Contains("EntityBundle", typeName ?? "");
            _output.WriteLine($"$Type: {typeName}");
        }
    }

    /// <summary>
    /// Tests that a SessionComponents VRB file can be deserialized to valid JSON.
    /// </summary>
    [Fact]
    public void VrbToJson_SessionComponents_ReturnsValidJson()
    {
        var vrbPath = _fixture.FindTestFile("sessioncomponents.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No sessioncomponents.vrb found. Skipping test.");
            return;
        }

        _output.WriteLine($"Testing: {vrbPath}");

        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SessionComponents, validate: false);

        Assert.NotNull(json);
        Assert.NotEmpty(json);
        _output.WriteLine($"JSON size: {json.Length:N0} characters");

        using var doc = JsonDocument.Parse(json);
        Assert.NotNull(doc);
    }

    #endregion

    #region JSON → VRB Tests

    /// <summary>
    /// Tests that JSON can be serialized back to a VRB file.
    /// </summary>
    [Fact]
    public void JsonToVrb_SaveGame_CreatesValidFile()
    {
        // Arrange
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        // First, get JSON from the VRB
        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: false);
        Assert.NotNull(json);

        var outputPath = Path.Combine(Path.GetTempPath(), $"test-savegame-{Guid.NewGuid():N}.vrb");

        try
        {
            // Act
            Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, TargetType.SaveGame);

            // Assert
            Assert.True(File.Exists(outputPath), "Output VRB file was not created");
            
            var fileInfo = new FileInfo(outputPath);
            Assert.True(fileInfo.Length > 0, "Output file is empty");
            _output.WriteLine($"Created VRB file: {fileInfo.Length:N0} bytes");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    /// <summary>
    /// Tests that JSON can be serialized with different compression methods.
    /// </summary>
    [Theory]
    [InlineData("None")]
    [InlineData("ZLib")]
    [InlineData("Brotli")]
    public void JsonToVrb_CompressionMethods_AllWork(string compressionMethod)
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        var json = Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: false);
        var outputPath = Path.Combine(Path.GetTempPath(), $"test-{compressionMethod}-{Guid.NewGuid():N}.vrb");

        try
        {
            Vrb.Core.Vrb.Service.SerializeJsonToVrb(json, outputPath, TargetType.SaveGame, compressionMethod);

            Assert.True(File.Exists(outputPath));
            var size = new FileInfo(outputPath).Length;
            _output.WriteLine($"{compressionMethod}: {size:N0} bytes");
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    #endregion

    #region Validation Tests

    /// <summary>
    /// Tests that the validation mode completes without errors.
    /// </summary>
    [Fact]
    public void Validation_SaveGame_Passes()
    {
        var vrbPath = _fixture.FindTestFile("savegame.vrb");
        if (vrbPath == null)
        {
            _output.WriteLine("No savegame.vrb found. Skipping test.");
            return;
        }

        _output.WriteLine($"Validating: {vrbPath}");

        // Act & Assert - should not throw
        var exception = Record.Exception(() => 
            Vrb.Core.Vrb.Service!.DeserializeVrb(vrbPath, TargetType.SaveGame, validate: true));

        Assert.Null(exception);
        _output.WriteLine("Validation passed without errors.");
    }

    #endregion

    #region TargetTypeHelper Tests

    /// <summary>
    /// Tests that TargetTypeHelper correctly identifies types from filenames.
    /// </summary>
    [Theory]
    [InlineData("savegame", TargetType.SaveGame)]
    [InlineData("SAVEGAME", TargetType.SaveGame)]
    [InlineData("my_savegame_backup", TargetType.SaveGame)]
    [InlineData("sessioncomponents", TargetType.SessionComponents)]
    [InlineData("SessionComponents", TargetType.SessionComponents)]
    [InlineData("assetjournal", TargetType.AssetJournal)]
    public void TargetTypeHelper_GetFromFilename_ReturnsCorrectType(string filename, TargetType expected)
    {
        var result = TargetTypeHelper.GetFromFilename(filename);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Tests that TargetTypeHelper returns null for unknown filenames.
    /// </summary>
    [Theory]
    [InlineData("unknown")]
    [InlineData("data")]
    [InlineData("")]
    public void TargetTypeHelper_GetFromFilename_ReturnsNullForUnknown(string filename)
    {
        var result = TargetTypeHelper.GetFromFilename(filename);
        Assert.Null(result);
    }

    #endregion
}

