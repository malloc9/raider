namespace Raider.Plugin.Chores;

using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Claim playtime rewards: collects every playtime gift whose timer has finished, without waiting for the rest.
/// <para>
/// Game side: <c>UserWrapper.HourlyGiftsWrapper</c> (Client.Model.Gameplay.SessionChests.HourlyGiftsWrapper).
/// <c>RegularRewardsToCollect(PlaytimeTodayMinutes(false))</c> lists the finished, untaken gifts by their minute
/// threshold; <c>TakeSessionChestCmd(minutes)</c> takes one. The Plarium Play program has its own row of gifts
/// (<c>PlariumPlayRewardsToCollect</c>, <c>TakePlariumProgramSessionChestCmd</c>).
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
        var regularMinutes = Convert.ToInt32(Call(gifts, "PlaytimeTodayMinutes", false));
        var plariumMinutes = Convert.ToInt32(Call(gifts, "PlaytimeTodayMinutes", true));

        var commands = new List<PlannedCommand>();
        foreach (var minutes in ThresholdsOf(Call(gifts, "RegularRewardsToCollect", regularMinutes)))
        {
            commands.Add(new PlannedCommand(TakeRegular, new object?[] { minutes }, new { gift = "Playtime", minutes }));
        }

        foreach (var minutes in ThresholdsOf(Call(gifts, "PlariumPlayRewardsToCollect", plariumMinutes)))
        {
            commands.Add(new PlannedCommand(TakePlarium, new object?[] { minutes }, new { gift = "PlariumPlay", minutes }));
        }

        var details = new { playtimeTodayMinutes = regularMinutes, plariumPlaytimeTodayMinutes = plariumMinutes };
        return commands.Count == 0 ? ChorePlan.Nothing("NoFinishedGifts", details) : new ChorePlan(commands, "", details);
    }

    /// <summary>The gifts to collect come as a Dictionary&lt;int minutes, UserPrize&gt;.</summary>
    private static IEnumerable<int> ThresholdsOf(object? prizesByMinutes) =>
        Items(prizesByMinutes).Select(entry => Convert.ToInt32(Get(entry, "Key"))).OrderBy(m => m).ToList();
}
