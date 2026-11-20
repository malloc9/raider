namespace Raider.Core.Services;

using Raider.Core.Models;

/// <summary>
/// Service for installing and managing the BepInEx plugin in the game folder.
/// </summary>
public interface IPluginInstallerService
{
    /// <summary>
    /// Installs or updates BepInEx and the Raider plugin in the game folder.
    /// Generates a per-install secret token for the plugin health endpoint.
    /// </summary>
    Task<PluginInstallResult> InstallAsync(
        GameInstallation gameInstallation,
        string token,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks the current installation state of the plugin.
    /// </summary>
    Task<PluginInstallState> GetInstallStateAsync(GameInstallation gameInstallation, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Uninstalls the plugin and BepInEx from the game folder.
    /// </summary>
    Task<bool> UninstallAsync(GameInstallation gameInstallation, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the token for an existing installation, or generates a new one.
    /// </summary>
    Task<string> GetOrCreateTokenAsync(GameInstallation gameInstallation, CancellationToken cancellationToken = default);
}