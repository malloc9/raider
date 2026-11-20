namespace Raider.Plugin.Chores;

using Raider.Plugin.Game;
using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Claim playtime rewards: collects every playtime gift whose timer has finished, without waiting for the rest.
/// <para>
/// Game side. The game's own helper, <c>UserWrapper.HourlyGiftsWrapper</c>, throws KeyNotFoundException(0) live on
/// its computed members, so the gift state is read from the data underneath it:
/// <list type="bullet">
/// <item>Progress: <c>UserWrapper.User.GameData.SessionChestData</c> (SharedModel.Meta.SessionChests.UserSessionChestData):
/// <c>TotalMsInGameToday</c>, <c>PrizeReceivedMinutes</c>, <c>UserLevelForSessionChest</c>; the Plarium Play
/// program's own row in <c>PlariumProgramChest</c> (when <c>HasPlariumProgramChest</c>).</item>
/// <item>Prizes: <c>HourlyGiftsWrapper._staticData.SessionChestInfos</c>, one SessionChestInfo per account-level band
/// (<c>MinUserLevel</c>..<c>MaxUserLevel</c>) with <c>UserPrizeByMinutes</c> and <c>PlariumProgramPrizesByMinutes</c>.</item>
/// </list>
/// A gift is ready when its minute threshold is at most the server-counted play time and it is not in the received
/// list. The server count lags the running session, so this never claims early; the game re-checks every claim.
/// <c>TakeSessionChestCmd(minutes)</c> / <c>TakePlariumProgramSessionChestCmd(minutes)</c> take one gift.
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
        var progress = GetRequired(GetRequired(GetRequired(userWrapper, "User"), "GameData"), "SessionChestData");
        var infos = Items(Get(GetRequired(GetRequired(userWrapper, "HourlyGiftsWrapper"), "_staticData"), "SessionChestInfos")).ToList();
        if (infos.Count == 0)
        {
            throw new GameStateException("the game has no playtime gift tables (StaticSessionChestData.SessionChestInfos is empty)");
        }

        var sessionChestLevel = Convert.ToInt32(Get(progress, "UserLevelForSessionChest"));
        var accountLevel = Convert.ToInt32(Get(GetRequired(userWrapper, "Account"), "Level"));
        // The level the server fixed for today's gifts; 0 before it has set one, then the account level decides
        var level = sessionChestLevel > 0 ? sessionChestLevel : accountLevel;
        var band = infos.FirstOrDefault(info =>
                Convert.ToInt32(Get(info, "MinUserLevel")) <= level && level <= Convert.ToInt32(Get(info, "MaxUserLevel")))
            ?? throw new GameStateException($"no playtime gift table covers level {level}");

        var commands = new List<PlannedCommand>();
        var details = new Dictionary<string, object?>
        {
            ["level"] = new { sessionChestLevel, accountLevel }
        };

        AddRow("Playtime", TakeRegular, progress, Get(band, "UserPrizeByMinutes"), commands, details);
        if (Convert.ToBoolean(Get(progress, "HasPlariumProgramChest")) && Get(progress, "PlariumProgramChest") is { } plarium)
        {
            AddRow("PlariumPlay", TakePlarium, plarium, Get(band, "PlariumProgramPrizesByMinutes"), commands, details);
        }

        return commands.Count == 0 ? ChorePlan.Nothing("NoFinishedGifts", details) : new ChorePlan(commands, "", details);
    }

    /// <summary>Plans one row of gifts; <paramref name="row"/> has TotalMsInGameToday and PrizeReceivedMinutes.</summary>
    private static void AddRow(
        string name, string command, object row, object? prizesByMinutes,
        List<PlannedCommand> commands, Dictionary<string, object?> details)
    {
        var playtime = (int)(Convert.ToInt64(Get(row, "TotalMsInGameToday")) / 60_000);
        var received = Items(Get(row, "PrizeReceivedMinutes")).Select(Convert.ToInt32).ToHashSet();
        var thresholds = Items(prizesByMinutes).Select(entry => Convert.ToInt32(Get(entry, "Key"))).OrderBy(m => m).ToList();
        var ready = thresholds.Where(minutes => minutes <= playtime && !received.Contains(minutes)).ToList();

        details[name] = new { playtimeTodayMinutes = playtime, thresholds, received, ready };
        commands.AddRange(ready.Select(minutes =>
            new PlannedCommand(command, new object?[] { minutes }, new { gift = name, minutes })));
    }
}
