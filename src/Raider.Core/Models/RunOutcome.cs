namespace Raider.Core.Models;

/// <summary>
/// The final outcome of a Run.
/// </summary>
public enum RunOutcome
{
    /// <summary>The Run completed all chores (some may have failed, but the Run finished).</summary>
    Completed,

    /// <summary>The Run was stopped because the Account switch needs a password.</summary>
    NeedsManualLogin,

    /// <summary>The Account switch failed for another reason.</summary>
    SwitchFailed,

    /// <summary>The Instance was lost mid-Run (process died or health check failed).</summary>
    InstanceLost,

    /// <summary>The Run was cancelled.</summary>
    Cancelled,

    /// <summary>The Run was skipped because the Instance was busy for more than 5 minutes.</summary>
    BusyTimeout,

    /// <summary>The Run was skipped because another Instance with the same PlayerId is running.</summary>
    Duplicate
}