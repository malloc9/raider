namespace Raider.Tests.Unit;

using Raider.Core.Models;
using Raider.Core.Services;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;

/// <summary>
/// Unit tests for PluginInstallerService.
/// </summary>
public sealed class PluginInstallerServiceTests
{
    private readonly PluginInstallerService _service;
    private readonly Mock<ILogger<PluginInstallerService>> _loggerMock;
    private readonly string _testGamePath;

    public PluginInstallerServiceTests()
    {
        _loggerMock = new Mock<ILogger<PluginInstallerService>>();
        _service = new PluginInstallerService(_loggerMock.Object);
        _testGamePath = Path.Combine(Path.GetTempPath(), "raider-test-game-" + Guid.NewGuid());
        Directory.CreateDirectory(_testGamePath);
        Directory.CreateDirectory(Path.Combine(_testGamePath, "Raid_Data"));
        File.WriteAllText(Path.Combine(_testGamePath, "Raid.exe"), "dummy");
    }

    [Fact]
    public void Constructor_CreatesInstance()
    {
        _service.Should().NotBeNull();
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_GeneratesNewToken_WhenNoneExists()
    {
        var installation = new GameInstallation(
            GamePath: _testGamePath,
            ExecutablePath: Path.Combine(_testGamePath, "Raid.exe"),
            DataPath: Path.Combine(_testGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token = await _service.GetOrCreateTokenAsync(installation);
        
        token.Should().NotBeNullOrEmpty();
        token.Length.Should().BeGreaterThan(20); // Base64 encoded 32 bytes
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_ReturnsExistingToken_WhenTokenFileExists()
    {
        var installation = new GameInstallation(
            GamePath: _testGamePath,
            ExecutablePath: Path.Combine(_testGamePath, "Raid.exe"),
            DataPath: Path.Combine(_testGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token1 = await _service.GetOrCreateTokenAsync(installation);
        var token2 = await _service.GetOrCreateTokenAsync(installation);
        
        token2.Should().Be(token1);
    }

    [Fact]
    public async Task GetInstallStateAsync_ReturnsNotInstalled_ForEmptyGameFolder()
    {
        var installation = new GameInstallation(
            GamePath: _testGamePath,
            ExecutablePath: Path.Combine(_testGamePath, "Raid.exe"),
            DataPath: Path.Combine(_testGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var state = await _service.GetInstallStateAsync(installation);
        
        state.Should().Be(PluginInstallState.NotInstalled);
    }

    [Fact]
    public async Task GenerateToken_ProducesValidBase64UrlString()
    {
        // Test the private GenerateToken method via reflection or just verify token format
        var installation = new GameInstallation(
            GamePath: _testGamePath,
            ExecutablePath: Path.Combine(_testGamePath, "Raid.exe"),
            DataPath: Path.Combine(_testGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token = await _service.GetOrCreateTokenAsync(installation);
        
        // Should be valid base64url (no +, /, =)
        token.Should().NotContain("+");
        token.Should().NotContain("/");
        token.Should().NotContain("=");
        token.Should().NotContain("=");
    }
}