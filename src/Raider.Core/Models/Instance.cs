namespace Raider.Core.Models;

/// <summary>
/// Represents a game instance discovered in the instances directory.
/// </summary>
public sealed record Instance(
    string Id,
    string Token,
    int ProcessId,
    string? PlayerId,
    string? PlayerName,
    DateTimeOffset LastSeen,
    InstanceStatus Status = InstanceStatus.Discovered
);

/// <summary>
/// Current status of an Instance.
/// </summary>
public enum InstanceStatus
{
    /// <summary>Instance was discovered but not yet validated.</summary>
    Discovered,

    /// <summary>Instance is healthy and ready for Runs.</summary>
    Ready,

    /// <summary>Instance is currently running a Run.</summary>
    Busy,

    /// <summary>Instance is waiting for a Busy instance to become available (max 5 min).</summary>
    Waiting,

    /// <summary>Another Instance with the same PlayerId is already running.</summary>
    Duplicate,

    /// <summary>Instance process died or health check failed.</summary>
    Dead,

    /// <summary>Instance was lost mid-Run.</summary>
    InstanceLost
}