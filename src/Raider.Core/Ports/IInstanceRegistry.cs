namespace Raider.Core.Ports;

using Raider.Core.Models;

/// <summary>
/// Discovers and tracks game Instances from the instances directory.
/// </summary>
public interface IInstanceRegistry : IAsyncDisposable
{
    /// <summary>
    /// Event raised when the set of known instances changes.
    /// </summary>
    event EventHandler<InstanceRegistryChangedEventArgs> Changed;

    /// <summary>
    /// Gets all currently known instances.
    /// </summary>
    IReadOnlyList<Instance> GetInstances();

    /// <summary>
    /// Gets instances that are ready for a Run (not busy, not dead, not duplicate).
    /// </summary>
    IReadOnlyList<Instance> GetReadyInstances();

    /// <summary>
    /// Marks an instance as busy (running a Run).
    /// </summary>
    void MarkBusy(string instanceId);

    /// <summary>
    /// Marks an instance as no longer busy.
    /// </summary>
    void MarkFree(string instanceId);

    /// <summary>
    /// Marks an instance as lost mid-Run.
    /// </summary>
    void MarkInstanceLost(string instanceId);

    /// <summary>
    /// Gets the instance for a given player ID, if any.
    /// </summary>
    Instance? GetInstanceByPlayerId(string playerId);

    /// <summary>
    /// Forces a refresh of the instances directory.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Arguments for the InstanceRegistry.Changed event.
/// </summary>
public sealed record InstanceRegistryChangedEventArgs(
    IReadOnlyList<Instance> Added,
    IReadOnlyList<Instance> Removed,
    IReadOnlyList<Instance> Updated
);