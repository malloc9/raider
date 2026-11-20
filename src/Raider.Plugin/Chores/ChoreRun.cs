namespace Raider.Plugin.Chores;

using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin;
using Raider.Plugin.Game;

/// <summary>
/// One game command a Chore plans to issue, with what it is for (reported back to Raider).
/// </summary>
internal sealed record PlannedCommand(
    string CommandType,
    object?[] Args,
    object Item,
    Func<string?>? Precheck = null,
    int Gems = 0);

/// <summary>
/// The shared shape of every Chore: read the player's state on the main thread, plan the game's own commands, then
/// issue them one at a time and report what happened.
/// <para>
/// Outcome contract (read by Raider's PluginGamePort): <c>Success</c> true with data.outcome "Success" or
/// "NothingToDo"; <c>Success</c> false with an error message when nothing planned could be done.
/// Passing <c>{"dryRun": true}</c> as a parameter returns the plan without issuing any command.
/// </para>
/// </summary>
internal static class ChoreRun
{
    /// <summary>Stop starting commands after this, so Raider's 60 s request timeout is not hit.</summary>
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Runs a Chore: <paramref name="plan"/> runs on the main thread against the player's UserWrapper and returns
    /// the commands to issue, or a reason there is nothing to do.
    /// </summary>
    public static async Task<ChoreHandlerResult> ExecuteAsync(
        string choreName,
        Dictionary<string, object>? parameters,
        Func<object, ChorePlan> plan)
    {
        var log = RaiderPlugin.Log;
        try
        {
            log.LogInfo($"{choreName}: starting");
            if (!GameAccess.IsInGame())
            {
                return Fail(log, choreName, "No player is logged in to the game yet");
            }

            var chorePlan = await MainThread.RunAsync(() =>
            {
                var userWrapper = GameAccess.GetUserWrapper()
                    ?? throw new GameStateException("the game has no logged-in player state (AppModel._userWrapper)");
                return plan(userWrapper);
            });

            if (chorePlan.Commands.Count == 0)
            {
                log.LogInfo($"{choreName}: nothing to do ({chorePlan.NothingToDoReason})");
                return new ChoreHandlerResult(true, null, new
                {
                    outcome = "NothingToDo",
                    reason = chorePlan.NothingToDoReason,
                    gemsCollected = 0,
                    details = chorePlan.Details
                });
            }

            if (IsDryRun(parameters))
            {
                log.LogInfo($"{choreName}: dry run, {chorePlan.Commands.Count} command(s) planned");
                return new ChoreHandlerResult(true, null, new
                {
                    outcome = "DryRun",
                    planned = chorePlan.Commands.Select(c => new { command = ShortName(c.CommandType), args = c.Args, item = c.Item }),
                    gemsCollected = 0,
                    details = chorePlan.Details
                });
            }

            var started = DateTime.UtcNow;
            var done = new List<PlannedCommand>();
            var failed = new List<Unfinished>();
            var skipped = new List<Unfinished>();
            var notAttempted = 0;
            foreach (var command in chorePlan.Commands)
            {
                if (DateTime.UtcNow - started > Budget)
                {
                    notAttempted++;
                    continue;
                }

                var result = await GameCommands.RunAsync(command.CommandType, command.Args, command.Precheck);
                if (result.Succeeded)
                {
                    done.Add(command);
                }
                else if (result.WasSkipped)
                {
                    skipped.Add(new Unfinished(command.Item, result.Error));
                }
                else
                {
                    failed.Add(new Unfinished(command.Item, result.Error));
                }
            }

            log.LogInfo($"{choreName}: {done.Count} done, {failed.Count} failed, {skipped.Count} skipped, {notAttempted} left for next time");

            if (done.Count == 0 && failed.Count > 0)
            {
                return Fail(log, choreName, $"Nothing could be done: {failed[0].Reason}", new { failed, skipped });
            }

            if (done.Count == 0)
            {
                return new ChoreHandlerResult(true, null, new
                {
                    outcome = "NothingToDo",
                    reason = "AllSkipped",
                    gemsCollected = 0,
                    skipped
                });
            }

            return new ChoreHandlerResult(true, null, new
            {
                outcome = "Success",
                done = done.Select(c => c.Item),
                failed,
                skipped,
                notAttempted,
                gemsCollected = done.Sum(c => c.Gems)
            });
        }
        catch (Exception ex)
        {
            log.LogError($"{choreName} failed: {ex}");
            return new ChoreHandlerResult(false, $"{choreName} failed: {ex.GetBaseException().Message}");
        }
    }

    private static ChoreHandlerResult Fail(ManualLogSource log, string choreName, string error, object? data = null)
    {
        log.LogWarning($"{choreName}: {error}");
        return new ChoreHandlerResult(false, error, data);
    }

    private static bool IsDryRun(Dictionary<string, object>? parameters) =>
        parameters != null
        && parameters.TryGetValue("dryRun", out var value)
        && value switch
        {
            bool b => b,
            JsonElement { ValueKind: JsonValueKind.True } => true,
            _ => false
        };

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];
}

/// <summary>A planned command that failed or was skipped, and why.</summary>
internal sealed record Unfinished(object Item, string? Reason);

/// <summary>What a Chore found it should do.</summary>
internal sealed record ChorePlan(IReadOnlyList<PlannedCommand> Commands, string NothingToDoReason, object? Details = null)
{
    public static ChorePlan Nothing(string reason, object? details = null) => new(Array.Empty<PlannedCommand>(), reason, details);
}
