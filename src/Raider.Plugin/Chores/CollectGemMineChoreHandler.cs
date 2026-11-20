namespace Raider.Plugin.Chores;

using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Collect Gem Mine: takes the gems accumulated in the Gem Mine.
/// <para>
/// Game side: <c>UserWrapper.Village.ProducedGemsCount</c> is what the mine holds;
/// <c>CollectGemsFromMineCmd()</c> takes it. The count read before collecting is reported as gems collected.
/// </para>
/// </summary>
internal sealed class CollectGemMineChoreHandler : IChoreHandler
{
    private const string Collect = "Client.Model.Gameplay.Village.Commands.CollectGemsFromMineCmd";

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Collect Gem Mine", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var village = GetRequired(userWrapper, "Village");
        var gems = Convert.ToInt32(Get(village, "ProducedGemsCount"));

        // The Gem Mine unlocks only at a higher account level; a mine with no capacity is not built yet
        int? capacity;
        try
        {
            capacity = Convert.ToInt32(Call(village, "GetGemsMineCapacity"));
        }
        catch (Exception)
        {
            capacity = null;
        }

        if (gems <= 0 && capacity is null or <= 0)
        {
            return ChorePlan.Nothing("GemMineNotUnlocked", new { capacity });
        }

        return gems <= 0
            ? ChorePlan.Nothing("NoGemsAccumulated", new { capacity })
            : new ChorePlan(new[] { new PlannedCommand(Collect, Array.Empty<object?>(), new { gems }, Gems: gems) }, "");
    }
}
