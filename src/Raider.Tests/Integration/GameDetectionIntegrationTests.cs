namespace Raider.Tests.Integration;

/// <summary>
/// GameDetectionService against a fake %LOCALAPPDATA% laid out like Plarium Play 10.x.
/// </summary>
public sealed class GameDetectionIntegrationTests : IDisposable
{
    private readonly string _localAppData = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
    private readonly string _plariumPlay;
    private readonly GameDetectionService _service;

    public GameDetectionIntegrationTests()
    {
        _plariumPlay = Path.Combine(_localAppData, "PlariumPlay");
        Directory.CreateDirectory(_plariumPlay);
        _service = new GameDetectionService(_localAppData);
    }

    public void Dispose()
    {
        if (Directory.Exists(_localAppData))
            Directory.Delete(_localAppData, recursive: true);
    }

    [Fact]
    public async Task FindsGame_ThroughGameStorageRecord()
    {
        var standAloneApps = Path.Combine(_plariumPlay, "StandAloneApps");
        var build = CreateGame(Path.Combine(standAloneApps, "raid-shadow-legends", "build"));
        WriteGameStorage(standAloneApps);

        var result = await _service.FindGameInstallationAsync();

        result.Should().NotBeNull();
        result!.GamePath.Should().Be(build);
        result.ExecutablePath.Should().Be(Path.Combine(build, "Raid.exe"));
        result.DataPath.Should().Be(Path.Combine(build, "Raid_Data"));
        result.IsPlariumPlay.Should().BeTrue();
    }

    [Fact]
    public async Task FindsGame_InCustomInstallationPathFromRecord()
    {
        var customRoot = Path.Combine(_localAppData, "Games", "Plarium");
        var build = CreateGame(Path.Combine(customRoot, "raid-shadow-legends", "build"));
        WriteGameStorage(customRoot);

        var result = await _service.FindGameInstallationAsync();

        result!.GamePath.Should().Be(build);
    }

    [Fact]
    public async Task FindsGame_InDefaultFolder_WhenRecordIsMissing()
    {
        var build = CreateGame(Path.Combine(_plariumPlay, "StandAloneApps", "raid-shadow-legends", "build"));

        var result = await _service.FindGameInstallationAsync();

        result!.GamePath.Should().Be(build);
    }

    [Fact]
    public async Task FindsGame_InDefaultFolder_WhenRecordIsCorrupt()
    {
        var build = CreateGame(Path.Combine(_plariumPlay, "StandAloneApps", "raid-shadow-legends", "build"));
        File.WriteAllText(Path.Combine(_plariumPlay, "gamestorage.gsfn"), "{not json");

        var result = await _service.FindGameInstallationAsync();

        result!.GamePath.Should().Be(build);
    }

    [Fact]
    public async Task IgnoresRecordEntries_ThatDoNotListRaid()
    {
        var otherRoot = Path.Combine(_localAppData, "Other");
        CreateGame(Path.Combine(otherRoot, "raid-shadow-legends", "build"));
        File.WriteAllText(Path.Combine(_plariumPlay, "gamestorage.gsfn"),
            "{\"InstalledGames\":{\"7\":{\"Id\":7,\"InstallationPath\":" + JsonSerializer.Serialize(otherRoot)
            + ",\"InsalledGames\":{\"some-other-game\":\"1\"}}}}");

        var result = await _service.FindGameInstallationAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task ReportsNewestPlariumPlayVersionFolder()
    {
        CreateGame(Path.Combine(_plariumPlay, "StandAloneApps", "raid-shadow-legends", "build"));
        Directory.CreateDirectory(Path.Combine(_plariumPlay, "10.17.0-0.0.1"));
        Directory.CreateDirectory(Path.Combine(_plariumPlay, "10.18.0-0.0.0"));
        Directory.CreateDirectory(Path.Combine(_plariumPlay, "Icons"));

        var result = await _service.FindGameInstallationAsync();

        result!.PlariumPlayVersion.Should().Be("10.18.0");
    }

    [Fact]
    public async Task ReturnsNull_WhenGameIsNotInstalled()
    {
        var result = await _service.FindGameInstallationAsync();

        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_AcceptsIl2CppBuildFolder()
    {
        var build = CreateGame(Path.Combine(_localAppData, "game"));

        (await _service.ValidateGameInstallationAsync(build)).Should().BeTrue();
    }

    [Fact]
    public async Task Validate_RejectsFolderWithoutRaidData()
    {
        var folder = Path.Combine(_localAppData, "incomplete");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Raid.exe"), "");

        (await _service.ValidateGameInstallationAsync(folder)).Should().BeFalse();
    }

    private static string CreateGame(string buildFolder)
    {
        Directory.CreateDirectory(Path.Combine(buildFolder, "Raid_Data", "il2cpp_data"));
        File.WriteAllText(Path.Combine(buildFolder, "Raid.exe"), "");
        return buildFolder;
    }

    private void WriteGameStorage(string installationPath)
    {
        // Same shape Plarium Play 10.18 writes, including its "InsalledGames" spelling
        var json = "{\"InstalledGames\":{\"101\":{\"Id\":101,\"IntegrationType\":2,\"InstallationPath\":"
            + JsonSerializer.Serialize(installationPath)
            + ",\"InsalledGames\":{\"raid-shadow-legends\":\"157786\"},\"SourceUrls\":{}}},\"Installations\":[]}";
        File.WriteAllText(Path.Combine(_plariumPlay, "gamestorage.gsfn"), json);
    }
}
