namespace Raider.Core.Services;

using Raider.Core.Models;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Installs and manages BepInEx 6 and the Raider plugin in the game folder.
/// </summary>
public sealed class PluginInstallerService : IPluginInstallerService
{
    private const string TokenFileName = "raider.token";
    private const string BepInExVersion = "6.0.0-pre.1"; // Pinned BepInEx 6 version
    private const string PluginVersion = "1.0.0";
    
    private readonly ILogger<PluginInstallerService> _logger;

    public PluginInstallerService(ILogger<PluginInstallerService> logger)
    {
        _logger = logger;
    }

    public async Task<PluginInstallResult> InstallAsync(
        GameInstallation gameInstallation,
        string token,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            progress?.Report("Preparing game folder...");
            _logger.LogInformation("Installing plugin to {GamePath}", gameInstallation.GamePath);

            var gamePath = gameInstallation.GamePath;
            var bepInExPath = Path.Combine(gamePath, "BepInEx");
            var pluginsPath = Path.Combine(bepInExPath, "plugins");
            var pluginPath = Path.Combine(pluginsPath, "Raider.Plugin.dll");

            // Create directory structure
            Directory.CreateDirectory(pluginsPath);
            Directory.CreateDirectory(Path.Combine(bepInExPath, "config"));
            Directory.CreateDirectory(Path.Combine(bepInExPath, "patchers"));
            Directory.CreateDirectory(Path.Combine(bepInExPath, "core"));

            progress?.Report("Installing BepInEx 6...");
            await InstallBepInExAsync(gamePath, cancellationToken);

            progress?.Report("Installing Raider plugin...");
            await InstallPluginAsync(pluginPath, cancellationToken);

            progress?.Report("Writing configuration...");
            await WriteBepInExConfigAsync(bepInExPath, token, cancellationToken);
            await WriteTokenAsync(gamePath, token, cancellationToken);

            progress?.Report("Installation complete.");

            _logger.LogInformation("Plugin installed successfully");
            return new PluginInstallResult(
                Success: true,
                State: PluginInstallState.Installed,
                InstalledPluginVersion: PluginVersion,
                InstalledBepInExVersion: BepInExVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install plugin");
            return new PluginInstallResult(
                Success: false,
                State: PluginInstallState.NotInstalled,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<PluginInstallState> GetInstallStateAsync(
        GameInstallation gameInstallation,
        CancellationToken cancellationToken = default)
    {
        var gamePath = gameInstallation.GamePath;
        var pluginPath = Path.Combine(gamePath, "BepInEx", "plugins", "Raider.Plugin.dll");
        var bepInExCorePath = Path.Combine(gamePath, "BepInEx", "core", "BepInEx.dll");
        var tokenPath = Path.Combine(gamePath, TokenFileName);

        if (!File.Exists(pluginPath) || !File.Exists(bepInExCorePath) || !File.Exists(tokenPath))
        {
            return PluginInstallState.NotInstalled;
        }

        // Check if plugin is connected by trying health endpoint
        // This will be done by the health client, so we just return Installed here
        return PluginInstallState.Installed;
    }

    public async Task<bool> UninstallAsync(
        GameInstallation gameInstallation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var bepInExPath = Path.Combine(gameInstallation.GamePath, "BepInEx");
            var tokenPath = Path.Combine(gameInstallation.GamePath, TokenFileName);

            if (Directory.Exists(bepInExPath))
            {
                Directory.Delete(bepInExPath, recursive: true);
            }

            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }

            _logger.LogInformation("Plugin uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to uninstall plugin");
            return false;
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

    private async Task InstallBepInExAsync(string gamePath, CancellationToken cancellationToken)
    {
        // In a real implementation, this would extract bundled BepInEx files
        // For now, we create the minimal structure and placeholder files
        // The actual BepInEx files would be embedded resources or bundled with the app
        
        var bepInExPath = Path.Combine(gamePath, "BepInEx");
        var corePath = Path.Combine(bepInExPath, "core");
        
        // Create placeholder BepInEx core assembly (in real app, this would be the actual BepInEx 6 DLL)
        var bepInExDll = Path.Combine(corePath, "BepInEx.dll");
        if (!File.Exists(bepInExDll))
        {
            // Write a minimal placeholder - real implementation embeds the actual BepInEx 6 build
            await File.WriteAllTextAsync(Path.Combine(corePath, "version.txt"), BepInExVersion, cancellationToken);
            _logger.LogWarning("BepInEx DLL placeholder created - real implementation should embed actual BepInEx 6 build");
        }

        // Create doorstop config for IL2CPP
        var doorstopConfig = Path.Combine(gamePath, "doorstop_config.ini");
        var doorstopContent = $"""
            [config]
            enabled=true
            target_assembly=BepInEx.core.BepInEx.Preloader
            plugin_path=BepInEx/plugins
            config_path=BepInEx/config
            """;
        await File.WriteAllTextAsync(doorstopConfig, doorstopContent, cancellationToken);

        // Create winhttp.dll placeholder (doorstop loader)
        var winHttpDll = Path.Combine(gamePath, "winhttp.dll");
        if (!File.Exists(winHttpDll))
        {
            _logger.LogWarning("winhttp.dll (doorstop) placeholder - real implementation should embed actual doorstop loader");
        }
    }

    private async Task InstallPluginAsync(string pluginPath, CancellationToken cancellationToken)
    {
        // In a real implementation, this would copy the built plugin DLL
        // For now, create a placeholder
        if (!File.Exists(pluginPath))
        {
            await File.WriteAllTextAsync(pluginPath + ".placeholder", 
                $"Raider Plugin v{PluginVersion} - Build artifact should be copied here", 
                cancellationToken);
            _logger.LogWarning("Plugin DLL placeholder created - real implementation should copy built plugin");
        }
    }

    private async Task WriteBepInExConfigAsync(string bepInExPath, string token, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(bepInExPath, "config", "BepInEx.cfg");
        var configContent = $"""
            [Preloader]
            ## List of assemblies to load before the game starts (comma separated)
            # Setting type: CommaSeparatedList
            # Default value: 
            PreloadAssemblies = 

            [Logging.Console]
            ## Whether to show the console window
            # Setting type: Boolean
            # Default value: true
            Enabled = true

            [Logging.Disk]
            ## Whether to write logs to disk
            # Setting type: Boolean
            # Default value: true
            Enabled = true

            [Raider.Plugin]
            ## Per-install secret token for health endpoint authentication
            # Setting type: String
            # Default value: 
            HealthToken = {token}

            ## Health endpoint port (bound to 127.0.0.1)
            # Setting type: Int32
            # Default value: 51837
            HealthPort = 51837
            """;
        await File.WriteAllTextAsync(configPath, configContent, cancellationToken);
    }

    private async Task WriteTokenAsync(string gamePath, string token, CancellationToken cancellationToken)
    {
        var tokenPath = Path.Combine(gamePath, TokenFileName);
        await File.WriteAllTextAsync(tokenPath, token, cancellationToken);
    }

    private static string GenerateToken()
    {
        // Generate a cryptographically secure random token (32 bytes = 256 bits)
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace('/', '_').Replace('+', '-').Replace("=", "");
    }
}