namespace Raider.Plugin.Chores;

using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Clan check-in: performs the daily Clan check-in.
/// <para>
/// Game side (the game calls a Clan an Alliance): <c>UserWrapper.Alliance.HasAlliance</c> says whether the player is
/// in one; <c>Alliance.Activity.CheckInAvailable</c> says whether today's check-in is still open;
/// <c>AllianceCheckInCmd()</c> checks in.
/// </para>
/// </summary>
internal sealed class ClanCheckInChoreHandler : IChoreHandler
{
    private const string CheckIn = "Client.Model.Gameplay.Alliance.Commands.AllianceCheckInCmd";

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Clan check-in", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var alliance = GetRequired(userWrapper, "Alliance");
        if (!Convert.ToBoolean(Get(alliance, "HasAlliance")))
        {
            return ChorePlan.Nothing("NotInClan");
        }

        var activity = GetRequired(alliance, "Activity");
        return Convert.ToBoolean(Get(activity, "CheckInAvailable"))
            ? new ChorePlan(new[] { new PlannedCommand(CheckIn, Array.Empty<object?>(), new { checkIn = "today" }) }, "")
            : ChorePlan.Nothing("AlreadyCheckedIn");
    }
}
