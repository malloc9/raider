namespace Raider.Core.Models;

/// <summary>
/// The ordered list of Chores configured for one Account.
/// </summary>
public sealed record Routine(
    IReadOnlyList<Chore> Chores
)
{
    /// <summary>
    /// The Routine a newly added Account starts with: every Chore, in glossary order.
    /// </summary>
    public static Routine Default() => new(new[]
    {
        new Chore("claim-playtime-rewards", "Claim playtime rewards", ChoreType.ClaimPlaytimeRewards),
        new Chore("claim-quests", "Claim quests", ChoreType.ClaimQuests),
        new Chore("collect-inbox", "Collect inbox", ChoreType.CollectInbox),
        new Chore("buy-free-market-items", "Buy free Market items", ChoreType.BuyFreeMarketItems),
        new Chore("collect-gem-mine", "Collect Gem Mine", ChoreType.CollectGemMine),
        new Chore("clan-check-in", "Clan check-in", ChoreType.ClanCheckIn),
    });
}