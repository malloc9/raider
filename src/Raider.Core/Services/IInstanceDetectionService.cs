namespace Raider.Core.Services;

using Raider.Core.Models;

/// <summary>
/// Service for detecting running Raid: Shadow Legends game instances.
/// </summary>
public interface IInstanceDetectionService
{
    /// <summary>
    /// Detects all currently running Raid game instances.
    /// </summary>
    Task<IReadOnlyList<Instance>> DetectInstancesAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken = default);
}