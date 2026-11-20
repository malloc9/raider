namespace Raider.Core.Services;

using System.IO.Compression;
using System.Security.Cryptography;
using Raider.Core.Models;

/// <summary>
/// Installs and manages BepInEx 6 (IL2CPP) and the Raider plugin in the game folder.
/// </summary>
public sealed class PluginInstallerService : IPluginInstallerService
{
    private const string TokenFileName = "raider.token";
    private const string PluginVersion = "1.0.0";
    private const string PluginGuid = "com.malloc9.raider";
    private const int HealthPort = 51837;

    /// <summary>Files and folders BepInEx's zip puts in the game folder; removed on uninstall.</summary>
    private static readonly string[] BepInExRootFiles = { "winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt" };
    private static readonly string[] BepInExRootFolders = { "BepInEx", "dotnet" };

    private readonly ILogger<PluginInstallerService> _logger;
    private readonly IBepInExPackageProvider _packageProvider;
    private readonly string _pluginDllPath;

    /// <param name="pluginDllPath">The built Raider.Plugin.dll; defaults to plugin\Raider.Plugin.dll next to the app.</param>
    public PluginInstallerService(
        ILogger<PluginInstallerService> logger,
        IBepInExPackageProvider? packageProvider = null,
        string? pluginDllPath = null)
    {
        _logger = logger;
        _packageProvider = packageProvider ?? new DownloadingBepInExPackageProvider();
        _pluginDllPath = pluginDllPath ?? Path.Combine(AppContext.BaseDirectory, "plugin", "Raider.Plugin.dll");
    }

    public async Task<PluginInstallResult> InstallAsync(
        GameInstallation gameInstallation,
        string token,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gamePath = gameInstallation.GamePath;
            _logger.LogInformation("Installing plugin to {GamePath}", gamePath);

            if (!File.Exists(_pluginDllPath))
            {
                return Failed($"Raider plugin not found at {_pluginDllPath}; rebuild or reinstall Raider");
            }

            progress?.Report("Getting BepInEx...");
            var packagePath = await _packageProvider.GetPackageAsync(progress, cancellationToken);

            progress?.Report($"Installing BepInEx {BepInExBuild.Version}...");
            RemovePlaceholderLeftovers(gamePath);
            ZipFile.ExtractToDirectory(packagePath, gamePath, overwriteFiles: true);

            progress?.Report("Installing Raider plugin...");
            var pluginsPath = Path.Combine(gamePath, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginsPath);
            File.Copy(_pluginDllPath, Path.Combine(pluginsPath, "Raider.Plugin.dll"), overwrite: true);

            progress?.Report("Writing configuration...");
            await WritePluginConfigAsync(gamePath, token, cancellationToken);
            await WriteTokenAsync(gamePath, token, cancellationToken);

            var missing = GetMissingFiles(gamePath);
            if (missing.Count > 0)
            {
                return Failed($"Installation incomplete, missing: {string.Join(", ", missing)}");
            }

            progress?.Report("Installation complete. Restart the game; its first start with BepInEx takes a few minutes.");
            _logger.LogInformation("Plugin installed successfully");
            return new PluginInstallResult(
                Success: true,
                State: PluginInstallState.Installed,
                InstalledPluginVersion: PluginVersion,
                InstalledBepInExVersion: BepInExBuild.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to install plugin");
            return Failed(ex.Message);
        }
    }

    public Task<PluginInstallState> GetInstallStateAsync(
        GameInstallation gameInstallation,
        CancellationToken cancellationToken = default)
    {
        // Connected is decided by the health client; here we only check the files are really there
        var state = GetMissingFiles(gameInstallation.GamePath).Count == 0
            ? PluginInstallState.Installed
            : PluginInstallState.NotInstalled;
        return Task.FromResult(state);
    }

    public Task<bool> UninstallAsync(
        GameInstallation gameInstallation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gamePath = gameInstallation.GamePath;
            foreach (var folder in BepInExRootFolders)
            {
                var path = Path.Combine(gamePath, folder);
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }

            foreach (var file in BepInExRootFiles.Append(TokenFileName))
            {
                var path = Path.Combine(gamePath, file);
                if (File.Exists(path))
                    File.Delete(path);
            }

            _logger.LogInformation("Plugin uninstalled successfully");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall plugin");
            return Task.FromResult(false);
        }
    }

    public async Task<string> GetOrCreateTokenAsync(
        GameInstallation gameInstallation,
        CancellationToken cancellationToken = default)
    {
        var tokenPath = Path.Combine(gameInstallation.GamePath, TokenFileName);

        if (File.Exists(tokenPath))
        {
            var existingToken = await File.ReadAllTextAsync(tokenPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(existingToken))
            {
                return existingToken.Trim();
            }
        }

        var newToken = GenerateToken();
        await WriteTokenAsync(gameInstallation.GamePath, newToken, cancellationToken);
        return newToken;
    }

    /// <summary>
    /// Files that must exist for BepInEx to load the plugin when the game starts.
    /// </summary>
    private List<string> GetMissingFiles(string gamePath)
    {
        var required = new[]
        {
            "winhttp.dll",
            "doorstop_config.ini",
            Path.Combine("BepInEx", "core", "BepInEx.Unity.IL2CPP.dll"),
            Path.Combine("dotnet", "coreclr.dll"),
            Path.Combine("BepInEx", "plugins", "Raider.Plugin.dll"),
            TokenFileName,
        };

        return required.Where(relative => !File.Exists(Path.Combine(gamePath, relative))).ToList();
    }

    /// <summary>
    /// Earlier Raider versions wrote placeholder files instead of a real install; remove them.
    /// </summary>
    private void RemovePlaceholderLeftovers(string gamePath)
    {
        var leftovers = new[]
        {
            Path.Combine(gamePath, "BepInEx", "core", "version.txt"),
            Path.Combine(gamePath, "BepInEx", "plugins", "Raider.Plugin.dll.placeholder"),
        };
        foreach (var path in leftovers.Where(File.Exists))
        {
            File.Delete(path);
        }

        // The placeholder BepInEx.cfg carried Raider's settings; BepInEx regenerates its own
        var bepInExConfig = Path.Combine(gamePath, "BepInEx", "config", "BepInEx.cfg");
        if (File.Exists(bepInExConfig) && File.ReadAllText(bepInExConfig).Contains("[Raider.Plugin]"))
        {
            File.Delete(bepInExConfig);
        }
    }

    /// <summary>
    /// The plugin reads its settings with Config.Bind, i.e. from BepInEx\config\&lt;plugin GUID&gt;.cfg.
    /// </summary>
    private static async Task WritePluginConfigAsync(string gamePath, string token, CancellationToken cancellationToken)
    {
        var configDirectory = Path.Combine(gamePath, "BepInEx", "config");
        Directory.CreateDirectory(configDirectory);

        var configContent = $"""
            [Raider.Plugin]

            ## Per-install secret token for health endpoint authentication
            # Setting type: String
            # Default value:
            HealthToken = {token}

            ## Health endpoint port (bound to 127.0.0.1)
            # Setting type: Int32
            # Default value: {HealthPort}
            HealthPort = {HealthPort}

            """;
        await File.WriteAllTextAsync(Path.Combine(configDirectory, $"{PluginGuid}.cfg"), configContent, cancellationToken);
    }

    private static async Task WriteTokenAsync(string gamePath, string token, CancellationToken cancellationToken)
    {
        var tokenPath = Path.Combine(gamePath, TokenFileName);
        await File.WriteAllTextAsync(tokenPath, token, cancellationToken);
    }

    private static PluginInstallResult Failed(string message) =>
        new(Success: false, State: PluginInstallState.NotInstalled, ErrorMessage: message);

    private static string GenerateToken()
    {
        // Cryptographically secure random token (32 bytes = 256 bits), base64url
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).Replace('/', '_').Replace('+', '-').Replace("=", "");
    }
}
