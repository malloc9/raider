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
        var gems = Convert.ToInt32(Get(GetRequired(userWrapper, "Village"), "ProducedGemsCount"));
        return gems <= 0
            ? ChorePlan.Nothing("NoGemsAccumulated")
            : new ChorePlan(new[] { new PlannedCommand(Collect, Array.Empty<object?>(), new { gems }, Gems: gems) }, "");
    }
}
