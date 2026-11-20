namespace Raider.Core.Models;

/// <summary>
/// Represents the health status of the in-game plugin.
/// </summary>
public sealed record PluginHealth(
    bool IsInGame,
    GameBuild GameBuild,
    string PluginVersion,
    DateTimeOffset Timestamp
);

/// <summary>
/// Represents the installation state of the plugin in the game folder.
/// </summary>
public enum PluginInstallState
{
    /// <summary>
    /// Plugin and BepInEx are not installed.
    /// </summary>
    NotInstalled,
    
    /// <summary>
    /// Plugin and BepInEx are installed but not yet connected (game not running or plugin not loaded).
    /// </summary>
    Installed,
    
    /// <summary>
    /// Plugin is installed, game is running, and health endpoint responded successfully.
    /// </summary>
    Connected
}

/// <summary>
/// Information about the Raid game installation.
/// </summary>
public sealed record GameInstallation(
    string GamePath,
    string ExecutablePath,
    string DataPath,
    bool IsPlariumPlay,
    string? PlariumPlayVersion = null
);

/// <summary>
/// Result of plugin installation.
/// </summary>
public sealed record PluginInstallResult(
    bool Success,
    PluginInstallState State,
    string? ErrorMessage = null,
    string? InstalledPluginVersion = null,
    string? InstalledBepInExVersion = null
);