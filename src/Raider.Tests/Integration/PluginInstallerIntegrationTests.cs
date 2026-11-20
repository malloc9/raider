namespace Raider.Tests.Integration;

using Raider.Core.Models;
using Raider.Core.Services;
using Xunit;
using FluentAssertions;
using System.IO;
using Microsoft.Extensions.Logging;
using Moq;

/// <summary>
/// Integration tests for PluginInstallerService against a temporary game folder.
/// </summary>
public sealed class PluginInstallerIntegrationTests
{
    private readonly PluginInstallerService _service;
    private readonly Mock<ILogger<PluginInstallerService>> _loggerMock;
    private string _tempGamePath = string.Empty;

    public PluginInstallerIntegrationTests()
    {
        _loggerMock = new Mock<ILogger<PluginInstallerService>>();
        _service = new PluginInstallerService(_loggerMock.Object);
    }

    [Fact]
    public async Task InstallAsync_InstallsPluginAndBepInEx_IntoCleanGameFolder()
    {
        // Arrange: Create a clean temporary game folder
        _tempGamePath = Path.Combine(Path.GetTempPath(), "raider-test-install-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempGamePath);
        Directory.CreateDirectory(Path.Combine(_tempGamePath, "Raid_Data"));
        File.WriteAllText(Path.Combine(_tempGamePath, "Raid.exe"), "dummy exe");

        var installation = new GameInstallation(
            GamePath: _tempGamePath,
            ExecutablePath: Path.Combine(_tempGamePath, "Raid.exe"),
            DataPath: Path.Combine(_tempGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token = "test-token-12345";

        // Act
        var result = await _service.InstallAsync(installation, token);

        // Assert
        result.Success.Should().BeTrue();
        result.State.Should().Be(PluginInstallState.Installed);
        result.InstalledPluginVersion.Should().Be("1.0.0");
        result.InstalledBepInExVersion.Should().Be("6.0.0-pre.1");

        // Verify files were created
        File.Exists(Path.Combine(_tempGamePath, "BepInEx", "plugins", "Raider.Plugin.dll.placeholder")).Should().BeTrue();
        File.Exists(Path.Combine(_tempGamePath, "BepInEx", "core", "version.txt")).Should().BeTrue();
        File.Exists(Path.Combine(_tempGamePath, "doorstop_config.ini")).Should().BeTrue();
        File.Exists(Path.Combine(_tempGamePath, "raider.token")).Should().BeTrue();
        
        // Verify token file content
        var tokenContent = await File.ReadAllTextAsync(Path.Combine(_tempGamePath, "raider.token"));
        tokenContent.Should().Be(token);

        // Verify BepInEx config contains token
        var configContent = await File.ReadAllTextAsync(Path.Combine(_tempGamePath, "BepInEx", "config", "BepInEx.cfg"));
        configContent.Should().Contain($"HealthToken = {token}");
        configContent.Should().Contain("HealthPort = 51837");

        // Cleanup
        Directory.Delete(_tempGamePath, true);
    }

    [Fact]
    public async Task GetInstallStateAsync_ReturnsInstalled_AfterInstallation()
    {
        // Arrange
        _tempGamePath = Path.Combine(Path.GetTempPath(), "raider-test-state-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempGamePath);
        Directory.CreateDirectory(Path.Combine(_tempGamePath, "Raid_Data"));
        File.WriteAllText(Path.Combine(_tempGamePath, "Raid.exe"), "dummy exe");

        var installation = new GameInstallation(
            GamePath: _tempGamePath,
            ExecutablePath: Path.Combine(_tempGamePath, "Raid.exe"),
            DataPath: Path.Combine(_tempGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token = "test-token-state";

        // Act: Install first
        await _service.InstallAsync(installation, token);

        // Act: Check state
        var state = await _service.GetInstallStateAsync(installation);

        // Assert
        state.Should().Be(PluginInstallState.Installed);

        // Cleanup
        Directory.Delete(_tempGamePath, true);
    }

    [Fact]
    public async Task GetOrCreateTokenAsync_GeneratesToken_AndPersistsIt()
    {
        // Arrange
        _tempGamePath = Path.Combine(Path.GetTempPath(), "raider-test-token-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempGamePath);
        Directory.CreateDirectory(Path.Combine(_tempGamePath, "Raid_Data"));
        File.WriteAllText(Path.Combine(_tempGamePath, "Raid.exe"), "dummy exe");

        var installation = new GameInstallation(
            GamePath: _tempGamePath,
            ExecutablePath: Path.Combine(_tempGamePath, "Raid.exe"),
            DataPath: Path.Combine(_tempGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        // Act
        var token1 = await _service.GetOrCreateTokenAsync(installation);
        var token2 = await _service.GetOrCreateTokenAsync(installation);

        // Assert
        token1.Should().NotBeNullOrEmpty();
        token2.Should().Be(token1); // Should return the same token

        // Cleanup
        Directory.Delete(_tempGamePath, true);
    }

    [Fact]
    public async Task UninstallAsync_RemovesPluginAndToken()
    {
        // Arrange
        _tempGamePath = Path.Combine(Path.GetTempPath(), "raider-test-uninstall-" + Guid.NewGuid());
        Directory.CreateDirectory(_tempGamePath);
        Directory.CreateDirectory(Path.Combine(_tempGamePath, "Raid_Data"));
        File.WriteAllText(Path.Combine(_tempGamePath, "Raid.exe"), "dummy exe");

        var installation = new GameInstallation(
            GamePath: _tempGamePath,
            ExecutablePath: Path.Combine(_tempGamePath, "Raid.exe"),
            DataPath: Path.Combine(_tempGamePath, "Raid_Data"),
            IsPlariumPlay: false);

        var token = "test-token-uninstall";

        // Act: Install first
        await _service.InstallAsync(installation, token);

        // Act: Uninstall
        var uninstalled = await _service.UninstallAsync(installation);

        // Assert
        uninstalled.Should().BeTrue();
        Directory.Exists(Path.Combine(_tempGamePath, "BepInEx")).Should().BeFalse();
        File.Exists(Path.Combine(_tempGamePath, "raider.token")).Should().BeFalse();
        File.Exists(Path.Combine(_tempGamePath, "doorstop_config.ini")).Should().BeFalse();

        // Cleanup
        Directory.Delete(_tempGamePath, true);
    }
}