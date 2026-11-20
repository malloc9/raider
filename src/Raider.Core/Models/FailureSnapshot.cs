namespace Raider.Core.Models;

/// <summary>
/// A snapshot of the game state when a Chore fails, saved next to the logs.
/// </summary>
public sealed record FailureSnapshot(
    string Id,
    string ChoreId,
    string ChoreName,
    ChoreOutcome Outcome,
    string ErrorMessage,
    string GameStateJson,
    string PluginLogExcerpt,
    DateTimeOffset CapturedAt
)
{
    public string FileName => $"{CapturedAt:yyyyMMdd-HHmmss}-{ChoreId}-{Outcome}.json";
}