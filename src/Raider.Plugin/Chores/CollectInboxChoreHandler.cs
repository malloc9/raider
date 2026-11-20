namespace Raider.Plugin.Chores;

using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Collect inbox: takes every inbox item that needs no choice; choice packs are left alone.
/// <para>
/// Game side: <c>UserWrapper.Inbox.Items</c>, a list of SharedModel.Meta.Inbox.InboxItem. An item whose prize lets
/// the player pick (<c>UserPrize.HasSelectableHeroes</c>) is a choice pack and is skipped; every other item is taken
/// with <c>CollectRewardItemCmd(itemId)</c>, the command the inbox's Collect button issues. The game's own checks
/// still apply (for example, a full artifact vault refuses an artifact).
/// </para>
/// </summary>
internal sealed class CollectInboxChoreHandler : IChoreHandler
{
    private const string Collect = "Client.Model.Gameplay.Inbox.Commands.CollectRewardItemCmd";

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Collect inbox", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var items = Items(Get(GetRequired(userWrapper, "Inbox"), "Items")).ToList();
        if (items.Count == 0)
        {
            return ChorePlan.Nothing("InboxEmpty");
        }

        var commands = new List<PlannedCommand>();
        var choicePacks = 0;
        foreach (var item in items)
        {
            var id = Convert.ToInt32(Get(item, "Id"));
            var type = Get(item, "TypeId")?.ToString();
            if (NeedsChoice(Get(item, "Prize")))
            {
                choicePacks++;
                continue;
            }

            commands.Add(new PlannedCommand(Collect, new object?[] { id }, new { itemId = id, type }));
        }

        var details = new { inboxItems = items.Count, choicePacksLeft = choicePacks };
        return commands.Count == 0 ? ChorePlan.Nothing("OnlyChoicePacks", details) : new ChorePlan(commands, "", details);
    }

    private static bool NeedsChoice(object? prize) =>
        prize != null && Convert.ToBoolean(Get(prize, "HasSelectableHeroes"));
}
