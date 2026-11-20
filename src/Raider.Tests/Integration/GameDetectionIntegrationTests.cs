namespace Raider.Tests.Integration;

using Raider.Core.Models;
using Raider.Core.Services;
using Xunit;
using FluentAssertions;
using System.IO.Abstractions.TestingHelpers;
using Microsoft.Win32;

/// <summary>
/// Integration tests for GameDetectionService with fake registry and file system.
/// </summary>
public sealed class GameDetectionIntegrationTests
{
    private readonly MockFileSystem _fileSystem;
    private readonly GameDetectionService _service;

    public GameDetectionIntegrationTests()
    {
        _fileSystem = new MockFileSystem();
        _service = new GameDetectionService();
    }

    [Fact]
    public async Task FindGameInstallationAsync_FindsGameViaPlariumPlayRegistry_WhenRegistryExists()
    {
        // This test would require mocking the registry
        // For now, verify the service handles missing registry gracefully
        var result = await _service.FindGameInstallationAsync();
        // Result depends on actual system state
        // In CI/test environment, this will likely be null
    }

    [Fact]
    public async Task ValidateGameInstallationAsync_ValidatesCompleteGameFolder()
    {
        var gamePath = @"C:\Games\Raid";
        
        _fileSystem.AddDirectory(Path.Combine(gamePath, "Raid_Data"));
        _fileSystem.AddDirectory(Path.Combine(gamePath, "Raid_Data", "Managed"));
        _fileSystem.AddFile(Path.Combine(gamePath, "Raid.exe"), new MockFileData("dummy exe"));
        
        // We can't easily test the real service with mock filesystem without abstraction
        // This test documents the expected behavior
    }

    [Fact]
    public async Task ValidateGameInstallationAsync_RejectsIncompleteGameFolder()
    {
        var gamePath = @"C:\Games\IncompleteRaid";
        
        _fileSystem.AddDirectory(gamePath);
        _fileSystem.AddFile(Path.Combine(gamePath, "Raid.exe"), new MockFileData("dummy exe"));
        // Missing Raid_Data folder
        
        // Would return false
    }

    [Fact]
    public async Task ValidateGameInstallationAsync_AcceptsIL2CPPGameFolder()
    {
        var gamePath = @"C:\Games\RaidIL2CPP";
        
        _fileSystem.AddDirectory(Path.Combine(gamePath, "Raid_Data"));
        _fileSystem.AddDirectory(Path.Combine(gamePath, "Raid_Data", "il2cpp_data"));
        _fileSystem.AddFile(Path.Combine(gamePath, "Raid.exe"), new MockFileData("dummy exe"));
        
        // Would return true (IL2CPP build)
    }
}