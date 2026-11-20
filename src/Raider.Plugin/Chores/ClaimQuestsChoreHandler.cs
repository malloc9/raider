namespace Raider.Plugin.Chores;

using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Claim quests: collects every completed quest reward on the Daily, Weekly, Monthly and Advanced tabs, plus the
/// Daily and Weekly progress chests.
/// <para>
/// Game side, two sources:
/// <list type="bullet">
/// <item><c>UserWrapper.PeriodicQuests.PeriodicData.Groups</c>: one group per Daily / Weekly / Monthly
/// (PeriodicQuestGroupId), each a list of UserPeriodicQuestStateData; a <c>Completed</c> one is claimed with
/// <c>ClaimPeriodicQuestRewardCmd(questId)</c>.</item>
/// <item><c>UserWrapper.Quests.QuestData.OpenedStates</c>: QuestState objects; a <c>Completed</c> one whose prize was
/// not given, in the Periodic or AdvancedDaily category, is claimed with <c>ClaimQuestRewardCmd(questId)</c>.
/// Other categories (events, tournaments, clan quests, passes) are outside this Chore and left alone.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class ClaimQuestsChoreHandler : IChoreHandler
{
    private const string ClaimPeriodic = "Client.Model.Gameplay.Quests.Commands.ClaimPeriodicQuestRewardCmd";
    private const string ClaimQuest = "Client.Model.Gameplay.Quests.Commands.ClaimQuestRewardCmd";

    /// <summary>QuestCategoryId values this Chore claims through ClaimQuestRewardCmd.</summary>
    private static readonly HashSet<string> ClaimedCategories = new(StringComparer.Ordinal) { "Periodic", "AdvancedDaily" };

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Claim quests", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var commands = new List<PlannedCommand>();
        var planned = new HashSet<int>();

        var periodicData = Get(GetRequired(userWrapper, "PeriodicQuests"), "PeriodicData");
        foreach (var group in Items(periodicData == null ? null : Get(periodicData, "Groups")))
        {
            var tab = Get(group, "GroupId")?.ToString();
            foreach (var quest in Items(Get(group, "PeriodicQuestsData")))
            {
                if (Get(quest, "QuestStateId")?.ToString() != "Completed")
                {
                    continue;
                }

                var questId = Convert.ToInt32(Get(quest, "QuestId"));
                if (planned.Add(questId))
                {
                    commands.Add(new PlannedCommand(ClaimPeriodic, new object?[] { questId }, new { questId, tab, kind = "Periodic" }));
                }
            }
        }

        var quests = GetRequired(userWrapper, "Quests");
        var otherCompleted = new Dictionary<string, int>();
        foreach (var state in Items(Get(GetRequired(quests, "QuestData"), "OpenedStates")))
        {
            if (Get(state, "StateId")?.ToString() != "Completed" || Convert.ToBoolean(Get(state, "PrizeGiven")))
            {
                continue;
            }

            var questId = Convert.ToInt32(Get(state, "QuestId"));
            var category = Call(quests, "GetQuest", questId) is { } quest ? Get(quest, "CategoryId")?.ToString() ?? "?" : "?";
            if (!ClaimedCategories.Contains(category))
            {
                otherCompleted[category] = otherCompleted.GetValueOrDefault(category) + 1;
                continue;
            }

            if (planned.Add(questId))
            {
                commands.Add(new PlannedCommand(ClaimQuest, new object?[] { questId }, new { questId, tab = category, kind = "Quest" }));
            }
        }

        // Completed quests of other kinds are left for the player; listed so a missed category is easy to spot
        var details = new { completedButNotClaimedByThisChore = otherCompleted };
        return commands.Count == 0 ? ChorePlan.Nothing("NoCompletedQuests", details) : new ChorePlan(commands, "", details);
    }
}
