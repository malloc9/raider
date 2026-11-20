namespace Raider.Core.Models;

/// <summary>
/// Represents a detected running game instance with its associated Account.
/// </summary>
public sealed record Instance(
    string Id,
    int ProcessId,
    string GamePath,
    string? AccountId,
    InstanceState State,
    DateTimeOffset? LastRunStartedAt,
    DateTimeOffset? LastRunCompletedAt,
    RunStatus? LastRunStatus,
    string? LastRunMessage);

/// <summary>
/// Current state of an Instance.
/// </summary>
public enum InstanceState
{
    /// <summary>Instance is idle, not running any routine.</summary>
    Idle,

    /// <summary>Instance is busy (running a routine).</summary>
    Busy,

    /// <summary>Instance is currently executing chores.</summary>
    Running,

    /// <summary>Instance completed its routine successfully today.</summary>
    DoneToday,

    /// <summary>Instance's last run failed.</summary>
    Failed,

    /// <summary>Another instance is already running this Account (duplicate).</summary>
    Duplicate
}