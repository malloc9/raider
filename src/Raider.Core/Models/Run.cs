namespace Raider.Core.Models;

/// <summary>
/// One execution of an Account's Routine, from switching to that Account to its summary of each Chore's outcome.
/// </summary>
public sealed record Run(
    string Id,
    Account Account,
    GameBuild TargetedGameBuild,
    GameBuild ActualGameBuild,
    IReadOnlyList<ChoreResult> Results,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    string LogDirectory,
    RunStatus Status = RunStatus.Completed,
    string? StatusMessage = null
)
{
    public bool HasFailures => Status is RunStatus.SwitchFailed or RunStatus.InstanceLost || Results.Any(r => r.IsFailure);

    /// <summary>The Run stopped before any Chore because the Account switch needs the player to log in by hand.</summary>
    public bool NeedsManualLogin => Status == RunStatus.NeedsManualLogin;

    /// <summary>The Run ended because the Instance was lost mid-run.</summary>
    public bool IsInstanceLost => Status == RunStatus.InstanceLost;

    public bool IsGameBuildMismatch => !TargetedGameBuild.Matches(ActualGameBuild);

    public int FailureCount => Results.Count(r => r.IsFailure);

    public int SuccessCount => Results.Count(r => r.Outcome == ChoreOutcome.Success);

    public int BrokenByGameUpdateCount => Results.Count(r => r.Outcome == ChoreOutcome.BrokenByGameUpdate);

    public TimeSpan Duration => CompletedAt - StartedAt;
}

/// <summary>
/// How far a Run got.
/// </summary>
public enum RunStatus
{
    /// <summary>The Account was active (or switched to) and every Chore in the Routine was attempted.</summary>
    Completed,

    /// <summary>The Account switch needs a password; the Run stopped and the player must log in by hand.</summary>
    NeedsManualLogin,

    /// <summary>The Account switch failed for another reason; no Chore was attempted.</summary>
    SwitchFailed,

    /// <summary>The Instance was lost mid-Run (process died or health check failed); Game day not marked done.</summary>
    InstanceLost
}