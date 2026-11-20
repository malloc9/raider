namespace Raider.Tests.Unit;

using Raider.Core.Models;
using Raider.Core.Services;
using Xunit;
using FluentAssertions;
using System.Text.Json;

/// <summary>
/// Unit tests for SnapshotService.
/// </summary>
public sealed class SnapshotServiceTests
{
    private readonly SnapshotService _service;
    private readonly string _tempDirectory;

    public SnapshotServiceTests()
    {
        _service = new SnapshotService();
        _tempDirectory = Path.Combine(Path.GetTempPath(), "raider-snapshot-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task SaveFailureSnapshotAsync_CreatesFileInLogDirectory()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("chore-1", "Test Chore");
        var choreResult = ChoreResult.Failed(chore, "Test error", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var gameStateJson = "{\"test\": \"data\"}";
        var pluginLogExcerpt = "Log line 1\nLog line 2";

        // Act
        var path = await _service.SaveFailureSnapshotAsync(_tempDirectory, choreResult, gameStateJson, pluginLogExcerpt);

        // Assert
        path.Should().StartWith(_tempDirectory);
        File.Exists(path).Should().BeTrue();

        var json = await File.ReadAllTextAsync(path);
        var snapshot = JsonSerializer.Deserialize<FailureSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        snapshot.Should().NotBeNull();
        snapshot!.ChoreId.Should().Be("chore-1");
        snapshot.ChoreName.Should().Be("Test Chore");
        snapshot.Outcome.Should().Be(ChoreOutcome.Failed);
        snapshot.ErrorMessage.Should().Be("Test error");
        snapshot.GameStateJson.Should().Be(gameStateJson);
        snapshot.PluginLogExcerpt.Should().Be(pluginLogExcerpt);
    }

    [Fact]
    public async Task SaveFailureSnapshotAsync_WithBrokenByGameUpdateOutcome_SavesCorrectOutcome()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("chore-2", "Another Chore");
        var choreResult = ChoreResult.BrokenByGameUpdate(chore, "Method not found", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var gameStateJson = "{}";
        var pluginLogExcerpt = "";

        // Act
        var path = await _service.SaveFailureSnapshotAsync(_tempDirectory, choreResult, gameStateJson, pluginLogExcerpt);

        // Assert
        File.Exists(path).Should().BeTrue();

        var json = await File.ReadAllTextAsync(path);
        var snapshot = JsonSerializer.Deserialize<FailureSnapshot>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        snapshot!.Outcome.Should().Be(ChoreOutcome.BrokenByGameUpdate);
    }

    [Fact]
    public async Task SaveFailureSnapshotAsync_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var newDir = Path.Combine(_tempDirectory, "subdir", "nested");
        var chore = TestDataBuilder.CreateChore("chore-3", "Test Chore");
        var choreResult = ChoreResult.Failed(chore, "Error", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        // Act
        var path = await _service.SaveFailureSnapshotAsync(newDir, choreResult, "{}", "");

        // Assert
        path.Should().StartWith(newDir);
        File.Exists(path).Should().BeTrue();
        Directory.Exists(newDir).Should().BeTrue();
    }
}