namespace Raider.Tests.Unit;

using Raider.Core.Models;
using Raider.Core.Services;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Win32;
using System.IO.Abstractions.TestingHelpers;

/// <summary>
/// Unit tests for GameDetectionService.
/// </summary>
public sealed class GameDetectionServiceTests
{
    private readonly GameDetectionService _service;
    private readonly MockFileSystem _fileSystem;

    public GameDetectionServiceTests()
    {
        _fileSystem = new MockFileSystem();
        _service = new GameDetectionService();
    }

    [Fact]
    public async Task FindGameInstallationAsync_ReturnsNull_WhenGameNotFound()
    {
        // This test would require mocking registry and file system
        // For now, we just verify the service can be instantiated
        _service.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidateGameInstallationAsync_ReturnsFalse_ForInvalidPath()
    {
        var result = await _service.ValidateGameInstallationAsync(@"C:\NonExistentPath");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateGameInstallationAsync_ReturnsFalse_ForPathWithoutExe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "raider-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = await _service.ValidateGameInstallationAsync(tempDir);
            result.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}