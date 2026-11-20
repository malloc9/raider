namespace Raider.Plugin.Chores;

using Raider.Plugin.Game;
using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Claim playtime rewards: collects every playtime gift whose timer has finished, without waiting for the rest.
/// <para>
/// Game side: <c>UserWrapper.HourlyGiftsWrapper</c> (Client.Model.Gameplay.SessionChests.HourlyGiftsWrapper).
/// <c>PrizesByMinutes</c>, <c>PrizeReceivedMinutes</c> and <c>TotalMsInGameToday</c> give the finished, untaken gifts by their minute
/// threshold; <c>TakeSessionChestCmd(minutes)</c> takes one. The Plarium Play program has its own row of gifts
/// (<c>PlariumProgramPrizesByMinutes</c>, <c>TakePlariumProgramSessionChestCmd</c>).
/// </para>
/// </summary>
internal sealed class ClaimPlaytimeRewardsChoreHandler : IChoreHandler
{
    private const string TakeRegular = "Client.Model.Gameplay.SessionChests.Commands.TakeSessionChestCmd";
    private const string TakePlarium = "Client.Model.Gameplay.SessionChests.Commands.TakePlariumProgramSessionChestCmd";

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Claim playtime rewards", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var gifts = GetRequired(userWrapper, "HourlyGiftsWrapper");
        var commands = new List<PlannedCommand>();
        var sources = new[]
        {
            new GiftRow("Playtime", TakeRegular, "TotalMsInGameToday", "PrizesByMinutes", "PrizeReceivedMinutes"),
            new GiftRow("PlariumPlay", TakePlarium, "TotalMsInPlariumPlayToday", "PlariumProgramPrizesByMinutes", "PlariumPlayPrizeReceivedMinutes")
        };

        var details = new Dictionary<string, object?>();
        foreach (var row in sources)
        {
            try
            {
                var (playtime, ready) = FinishedGifts(gifts, row);
                details[row.Name] = new { playtimeTodayMinutes = playtime, ready };
                commands.AddRange(ready.Select(minutes =>
                    new PlannedCommand(row.Command, new object?[] { minutes }, new { gift = row.Name, minutes })));
            }
            catch (Exception ex)
            {
                // One row of gifts the game cannot answer for (e.g. the Plarium Play program not running) must not
                // stop the other
                details[row.Name] = new { error = ex.GetBaseException().Message };
            }
        }

        if (commands.Count == 0 && details.Values.All(d => d?.GetType().GetProperty("error") != null))
        {
            // Not "nothing to do": the gift state could not be read at all
            throw new GameStateException($"could not read the playtime gifts: {System.Text.Json.JsonSerializer.Serialize(details)}");
        }

        return commands.Count == 0 ? ChorePlan.Nothing("NoFinishedGifts", details) : new ChorePlan(commands, "", details);
    }

    /// <summary>
    /// The gift thresholds (in minutes played today) that are reached and not yet taken. Read from the raw tables
    /// (prizes by minutes, minutes already received, server-counted play time) rather than the game's
    /// PlaytimeTodayMinutes / RewardsToCollect helpers, which throw KeyNotFoundException(0) live. The server-counted
    /// time lags behind the current session, so this never claims early; the game re-checks every claim anyway.
    /// </summary>
    private static (int Playtime, List<int> Ready) FinishedGifts(object gifts, GiftRow row)
    {
        var playtime = (int)(Convert.ToInt64(Get(gifts, row.PlayedMsMember)) / 60_000);
        var received = Items(Get(gifts, row.ReceivedMember)).Select(Convert.ToInt32).ToHashSet();
        var ready = Items(Get(gifts, row.PrizesMember))
            .Select(entry => Convert.ToInt32(Get(entry, "Key")))
            .Where(minutes => minutes <= playtime && !received.Contains(minutes))
            .OrderBy(minutes => minutes)
            .ToList();
        return (playtime, ready);
    }

    private sealed record GiftRow(string Name, string Command, string PlayedMsMember, string PrizesMember, string ReceivedMember);
}
