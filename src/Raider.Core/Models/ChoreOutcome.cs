namespace Raider.Core.Models;

/// <summary>
/// The outcome of a Chore execution.
/// </summary>
public enum ChoreOutcome
{
    /// <summary>The chore completed successfully.</summary>
    Success,
    
    /// <summary>The chore failed due to an error.</summary>
    Failed,
    
    /// <summary>The chore failed because the game build differs from what the plugin targets.</summary>
    BrokenByGameUpdate
}

/// <summary>
/// Result of a single Chore execution.
/// </summary>
public sealed record ChoreResult(
    Chore Chore,
    ChoreOutcome Outcome,
    string? ErrorMessage = null,
    string? SnapshotPath = null,
    DateTimeOffset StartedAt = default,
    DateTimeOffset CompletedAt = default
)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
    
    public bool IsFailure => Outcome is ChoreOutcome.Failed or ChoreOutcome.BrokenByGameUpdate;
    
    public static ChoreResult Success(Chore chore, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(chore, ChoreOutcome.Success, null, null, startedAt, completedAt);
    
    public static ChoreResult Failed(Chore chore, string errorMessage, string? snapshotPath, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(chore, ChoreOutcome.Failed, errorMessage, snapshotPath, startedAt, completedAt);
    
    public static ChoreResult BrokenByGameUpdate(Chore chore, string errorMessage, string? snapshotPath, DateTimeOffset startedAt, DateTimeOffset completedAt) =>
        new(chore, ChoreOutcome.BrokenByGameUpdate, errorMessage, snapshotPath, startedAt, completedAt);
}