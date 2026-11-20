namespace Raider.Core.Models;

/// <summary>
/// One execution of an Account's Routine, from attaching to that Account's Instance to its summary of each Chore's outcome.
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
    public bool HasFailures => Results.Any(r => r.IsFailure);

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
    /// <summary>Every Chore in the Routine was attempted.</summary>
    Completed
}
