namespace Raider.Core.Services;

using Microsoft.Win32;
using Raider.Core.Models;
using System.Diagnostics;

/// <summary>
/// Finds Raid: Shadow Legends installation via Plarium Play's game storage record.
/// Falls back to the default StandAloneApps location.
/// </summary>
public sealed class GameDetectionService : IGameDetectionService
{
    private const string PlariumPlayRegistryKey = @"Software\Plarium\PlariumPlay";
    private const string GameStorageValueName = "GameStorage";
    private const string RaidGameId = "raid";
    private const string StandAloneAppsSubPath = @"StandAloneApps\raid";

    public async Task<GameInstallation?> FindGameInstallationAsync(CancellationToken cancellationToken = default)
    {
        // Try Plarium Play registry first
        var plariumPlayPath = GetPlariumPlayGamePath();
        if (!string.IsNullOrEmpty(plariumPlayPath) && await ValidateGameInstallationAsync(plariumPlayPath, cancellationToken))
        {
            var version = GetPlariumPlayVersion();
            return new GameInstallation(
                GamePath: plariumPlayPath,
                ExecutablePath: Path.Combine(plariumPlayPath, "Raid.exe"),
                DataPath: Path.Combine(plariumPlayPath, "Raid_Data"),
                IsPlariumPlay: true,
                PlariumPlayVersion: version);
        }

        // Fallback to default StandAloneApps location
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var fallbackPath = Path.Combine(localAppData, StandAloneAppsSubPath);
        if (await ValidateGameInstallationAsync(fallbackPath, cancellationToken))
        {
            return new GameInstallation(
                GamePath: fallbackPath,
                ExecutablePath: Path.Combine(fallbackPath, "Raid.exe"),
                DataPath: Path.Combine(fallbackPath, "Raid_Data"),
                IsPlariumPlay: false);
        }

        return null;
    }

    public async Task<bool> ValidateGameInstallationAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return false;

        // Check for Raid.exe and Raid_Data folder
        var exePath = Path.Combine(gamePath, "Raid.exe");
        var dataPath = Path.Combine(gamePath, "Raid_Data");
        
        if (!File.Exists(exePath) || !Directory.Exists(dataPath))
            return false;

        // Check for IL2CPP metadata (indicates IL2CPP build)
        var il2cppDataPath = Path.Combine(dataPath, "il2cpp_data");
        var managedPath = Path.Combine(dataPath, "Managed");
        
        // Either IL2CPP or Mono is fine
        return Directory.Exists(il2cppDataPath) || Directory.Exists(managedPath);
    }

    private string? GetPlariumPlayGamePath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PlariumPlayRegistryKey);
            if (key == null)
                return null;

            var gameStorageJson = key.GetValue(GameStorageValueName) as string;
            if (string.IsNullOrEmpty(gameStorageJson))
                return null;

            // Parse the game storage JSON to find Raid's path
            // The structure is typically: { "games": { "raid": { "path": "..." } } }
            using var doc = System.Text.Json.JsonDocument.Parse(gameStorageJson);
            if (doc.RootElement.TryGetProperty("games", out var games) &&
                games.TryGetProperty(RaidGameId, out var raid) &&
                raid.TryGetProperty("path", out var pathElement))
            {
                var path = pathElement.GetString();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    return path;
                }
            }
        }
        catch (Exception)
        {
            // Ignore registry/parsing errors, fall back to default
        }

        return null;
    }

    private string? GetPlariumPlayVersion()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PlariumPlayRegistryKey);
            if (key == null)
                return null;

            var version = key.GetValue("Version") as string;
            return version;
        }
        catch
        {
            return null;
        }
    }
}