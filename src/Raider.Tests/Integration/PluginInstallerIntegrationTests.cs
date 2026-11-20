namespace Raider.Tests.Integration;

using System.IO.Compression;
using System.Net;
using System.Net.Http;
using Moq;

/// <summary>
/// PluginInstallerService against a temporary game folder and a fake BepInEx package laid out like the real one.
/// </summary>
public sealed class PluginInstallerIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
    private readonly string _gamePath;
    private readonly string _pluginDll;
    private readonly GameInstallation _installation;
    private readonly FakePackageProvider _packageProvider;
    private readonly PluginInstallerService _service;

    public PluginInstallerIntegrationTests()
    {
        _gamePath = Path.Combine(_root, "game");
        Directory.CreateDirectory(Path.Combine(_gamePath, "Raid_Data", "il2cpp_data"));
        File.WriteAllText(Path.Combine(_gamePath, "Raid.exe"), "exe");

        _pluginDll = Path.Combine(_root, "app", "plugin", "Raider.Plugin.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(_pluginDll)!);
        File.WriteAllText(_pluginDll, "plugin");

        _packageProvider = new FakePackageProvider(CreateFakeBepInExZip(Path.Combine(_root, "bepinex.zip")));
        _service = new PluginInstallerService(new Mock<ILogger<PluginInstallerService>>().Object, _packageProvider, _pluginDll);

        _installation = new GameInstallation(_gamePath, Path.Combine(_gamePath, "Raid.exe"), Path.Combine(_gamePath, "Raid_Data"), true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Install_PutsBepInExLoaderAndPluginIntoGameFolder()
    {
        var result = await _service.InstallAsync(_installation, "token-123");

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.InstalledBepInExVersion.Should().Be(BepInExBuild.Version);
        File.Exists(Path.Combine(_gamePath, "winhttp.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_gamePath, "doorstop_config.ini")).Should().BeTrue();
        File.Exists(Path.Combine(_gamePath, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_gamePath, "dotnet", "coreclr.dll")).Should().BeTrue();
        File.ReadAllText(Path.Combine(_gamePath, "BepInEx", "plugins", "Raider.Plugin.dll")).Should().Be("plugin");
    }

    [Fact]
    public async Task Install_WritesTokenWhereThePluginReadsIt()
    {
        await _service.InstallAsync(_installation, "token-123");

        var pluginConfig = File.ReadAllText(Path.Combine(_gamePath, "BepInEx", "config", "com.malloc9.raider.cfg"));
        pluginConfig.Should().Contain("[Raider.Plugin]");
        pluginConfig.Should().Contain("HealthToken = token-123");
        pluginConfig.Should().NotContain("HealthPort");
        File.ReadAllText(Path.Combine(_gamePath, "raider.token")).Should().Be("token-123");
    }

    [Fact]
    public async Task Install_Fails_WhenPluginDllIsMissing()
    {
        File.Delete(_pluginDll);

        var result = await _service.InstallAsync(_installation, "token-123");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Raider plugin not found");
        _packageProvider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Install_Fails_WhenPackageCannotBeObtained()
    {
        var service = new PluginInstallerService(
            new Mock<ILogger<PluginInstallerService>>().Object,
            new ThrowingPackageProvider(new HttpRequestException("offline")),
            _pluginDll);

        var result = await service.InstallAsync(_installation, "token-123");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("offline");
    }

    [Fact]
    public async Task Install_RemovesPlaceholderLeftoversFromEarlierVersions()
    {
        // What the old placeholder installer left behind
        Directory.CreateDirectory(Path.Combine(_gamePath, "BepInEx", "core"));
        Directory.CreateDirectory(Path.Combine(_gamePath, "BepInEx", "plugins"));
        Directory.CreateDirectory(Path.Combine(_gamePath, "BepInEx", "config"));
        File.WriteAllText(Path.Combine(_gamePath, "BepInEx", "core", "version.txt"), "6.0.0-pre.1");
        File.WriteAllText(Path.Combine(_gamePath, "BepInEx", "plugins", "Raider.Plugin.dll.placeholder"), "x");
        File.WriteAllText(Path.Combine(_gamePath, "BepInEx", "config", "BepInEx.cfg"), "[Raider.Plugin]\nHealthToken = old");

        await _service.InstallAsync(_installation, "token-123");

        File.Exists(Path.Combine(_gamePath, "BepInEx", "core", "version.txt")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "BepInEx", "plugins", "Raider.Plugin.dll.placeholder")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "BepInEx", "config", "BepInEx.cfg")).Should().BeFalse();
    }

    [Fact]
    public async Task InstallState_IsInstalled_AfterRealInstall()
    {
        await _service.InstallAsync(_installation, "token-123");

        (await _service.GetInstallStateAsync(_installation)).Should().Be(PluginInstallState.Installed);
    }

    [Fact]
    public async Task InstallState_IsNotInstalled_ForPlaceholderOnlyFolder()
    {
        Directory.CreateDirectory(Path.Combine(_gamePath, "BepInEx", "plugins"));
        File.WriteAllText(Path.Combine(_gamePath, "BepInEx", "plugins", "Raider.Plugin.dll.placeholder"), "x");
        File.WriteAllText(Path.Combine(_gamePath, "raider.token"), "t");

        (await _service.GetInstallStateAsync(_installation)).Should().Be(PluginInstallState.NotInstalled);
    }

    [Fact]
    public async Task Uninstall_RemovesEverythingTheInstallAdded()
    {
        await _service.InstallAsync(_installation, "token-123");

        (await _service.UninstallAsync(_installation)).Should().BeTrue();

        Directory.Exists(Path.Combine(_gamePath, "BepInEx")).Should().BeFalse();
        Directory.Exists(Path.Combine(_gamePath, "dotnet")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "winhttp.dll")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "doorstop_config.ini")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "raider.token")).Should().BeFalse();
        File.Exists(Path.Combine(_gamePath, "Raid.exe")).Should().BeTrue("the game itself is untouched");
    }

    [Fact]
    public async Task GetOrCreateToken_GeneratesOnceAndPersists()
    {
        var token1 = await _service.GetOrCreateTokenAsync(_installation);
        var token2 = await _service.GetOrCreateTokenAsync(_installation);

        token1.Should().NotBeNullOrEmpty();
        token2.Should().Be(token1);
    }

    [Fact]
    public async Task PackageProvider_RejectsDownloadWithWrongHash()
    {
        var handler = new StaticHandler(new byte[] { 1, 2, 3 });
        var provider = new DownloadingBepInExPackageProvider(Path.Combine(_root, "cache"), new HttpClient(handler));

        var act = () => provider.GetPackageAsync();

        await act.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(_root, "cache", BepInExBuild.FileName)).Should().BeFalse();
    }

    private static string CreateFakeBepInExZip(string zipPath)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var entry in new[]
                 {
                     "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt",
                     "BepInEx/core/BepInEx.Unity.IL2CPP.dll", "BepInEx/core/BepInEx.Core.dll",
                     "dotnet/coreclr.dll",
                 })
        {
            using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
            writer.Write(entry);
        }

        zip.CreateEntry("BepInEx/plugins/");
        return zipPath;
    }

    private sealed class FakePackageProvider : IBepInExPackageProvider
    {
        private readonly string _zipPath;

        public FakePackageProvider(string zipPath) => _zipPath = zipPath;

        public int Calls { get; private set; }

        public Task<string> GetPackageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(_zipPath);
        }
    }

    private sealed class ThrowingPackageProvider : IBepInExPackageProvider
    {
        private readonly Exception _exception;

        public ThrowingPackageProvider(Exception exception) => _exception = exception;

        public Task<string> GetPackageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            => Task.FromException<string>(_exception);
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly byte[] _body;

        public StaticHandler(byte[] body) => _body = body;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_body) });
    }
}
