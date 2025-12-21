using Vrb.Core;
using Vrb.Core.Utils;
using Xunit.Abstractions;

namespace Vrb.Tests;

/// <summary>
/// Tests for the DefinitionsHelper which loads game definitions.
/// TDD: These tests define the expected behavior of the DefinitionsHelper.
/// </summary>
public class DefinitionHelperTests : IClassFixture<VrbTestFixture>
{
    private readonly ITestOutputHelper _output;
    private readonly VrbTestFixture _fixture;

    public DefinitionHelperTests(VrbTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Tests that definitions can be loaded from the game's Content folder.
    /// The game stores definition metadata in definitionsets.vrb files.
    /// </summary>
    [Fact]
    public void CanLoadDefinitionsFromGame()
    {
        // Arrange - ensure VRB is initialized
        Assert.True(_fixture.IsInitialized, $"VRB initialization failed: {_fixture.InitializationError}");

        // Act - load definitions
        var definitionsHelper = DefinitionsHelper.Instance;

        // Assert - we should have some definitions loaded
        Assert.True(definitionsHelper.IsLoaded, "Definitions should be loaded");
        Assert.True(definitionsHelper.DefinitionCount > 0, $"Expected definitions to be loaded, but got {definitionsHelper.DefinitionCount}");

        _output.WriteLine($"Loaded {definitionsHelper.DefinitionCount} definitions");
    }

    /// <summary>
    /// Tests that we can find a specific definition type by searching for types 
    /// that match ProgressionCheckpointDefinition - the abstract type that was causing issues.
    /// </summary>
    [Fact]
    public void CanFindProgressionRelatedDefinitions()
    {
        // Arrange
        Assert.True(_fixture.IsInitialized, $"VRB initialization failed: {_fixture.InitializationError}");

        var definitionsHelper = DefinitionsHelper.Instance;
        Assert.True(definitionsHelper.IsLoaded, "Definitions should be loaded");

        // Act - find all definitions whose type name contains "Progression"
        var progressionDefinitions = definitionsHelper.FindDefinitionsByTypeName("Progression");

        // Assert - we should find some progression-related definitions
        Assert.NotNull(progressionDefinitions);
        
        _output.WriteLine($"Found {progressionDefinitions.Count} progression-related definitions:");
        foreach (var (guid, type) in progressionDefinitions.Take(10))
        {
            _output.WriteLine($"  {guid}: {type.FullName}");
        }

        // The key insight: for ProgressionCheckpointDefinition, we should find
        // concrete types (like BlockProgressionCheckpointDefinition), not the abstract type
        var concreteProgressionTypes = progressionDefinitions
            .Where(kvp => !kvp.Value.IsAbstract)
            .ToList();

        _output.WriteLine($"Of these, {concreteProgressionTypes.Count} are concrete (non-abstract) types");
        Assert.True(concreteProgressionTypes.Count > 0, "Expected to find at least one concrete progression definition type");
    }

    /// <summary>
    /// Tests that TryGetDefinitionType returns the correct type for a known GUID.
    /// </summary>
    [Fact]
    public void TryGetDefinitionType_ReturnsConcreteType()
    {
        // Arrange
        Assert.True(_fixture.IsInitialized, $"VRB initialization failed: {_fixture.InitializationError}");

        var definitionsHelper = DefinitionsHelper.Instance;
        Assert.True(definitionsHelper.IsLoaded, "Definitions should be loaded");

        // Act - get any definition and verify we can look it up
        var allDefinitions = definitionsHelper.GetAllDefinitions();
        Assert.True(allDefinitions.Count > 0, "Expected at least one definition");

        var firstDef = allDefinitions.First();
        var success = definitionsHelper.TryGetDefinitionType(firstDef.Key, out var type);

        // Assert
        Assert.True(success, $"Failed to find type for GUID {firstDef.Key}");
        Assert.NotNull(type);
        Assert.Equal(firstDef.Value, type);

        _output.WriteLine($"Successfully looked up: {firstDef.Key} -> {type!.FullName}");
    }

    /// <summary>
    /// Tests that TryGetDefinitionType returns false for unknown GUIDs.
    /// </summary>
    [Fact]
    public void TryGetDefinitionType_ReturnsFalseForUnknownGuid()
    {
        // Arrange
        Assert.True(_fixture.IsInitialized, $"VRB initialization failed: {_fixture.InitializationError}");

        var definitionsHelper = DefinitionsHelper.Instance;
        var unknownGuid = Guid.NewGuid();

        // Act
        var success = definitionsHelper.TryGetDefinitionType(unknownGuid, out var type);

        // Assert
        Assert.False(success, "Should return false for unknown GUID");
        Assert.Null(type);
    }
}

