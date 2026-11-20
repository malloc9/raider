namespace Raider.Core.Models;

/// <summary>
/// One Raid: Shadow Legends player profile.
/// Identified by the player ID (assigned by the game server).
/// </summary>
public sealed record Account(
    string Id,
    string DisplayName,
    Routine Routine,
    int LastRunGameDay = 0,
    RunOutcome LastOutcome = RunOutcome.Completed
);

/// <summary>
/// Utility for Game Day calculations.
/// Raid's daily reset is at 00:00 UTC.
/// </summary>
public static class GameDay
{
    /// <summary>
    /// Gets the current Game Day number (days since Unix epoch at 00:00 UTC).
    /// </summary>
    public static int Current() => (int)(DateTimeOffset.UtcNow.Date - DateTimeOffset.UnixEpoch).TotalDays;

    /// <summary>
    /// Checks if an account has already run today (this Game Day).
    /// </summary>
    public static bool HasRunToday(Account account) => account.LastRunGameDay == Current();

    /// <summary>
    /// Creates an Account with an updated LastRunGameDay and LastOutcome.
    /// </summary>
    public static Account WithRunResult(Account account, RunOutcome outcome) =>
        account with { LastRunGameDay = Current(), LastOutcome = outcome };
}