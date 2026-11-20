namespace Raider.Core.Services;

using Raider.Core.Models;

/// <summary>
/// Service for detecting Raid: Shadow Legends installation via Plarium Play.
/// </summary>
public interface IGameDetectionService
{
    /// <summary>
    /// Finds the Raid game installation.
    /// First checks Plarium Play's game storage record, then falls back to default StandAloneApps location.
    /// </summary>
    Task<GameInstallation?> FindGameInstallationAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validates that the given path contains a valid Raid installation.
    /// </summary>
    Task<bool> ValidateGameInstallationAsync(string gamePath, CancellationToken cancellationToken = default);
}