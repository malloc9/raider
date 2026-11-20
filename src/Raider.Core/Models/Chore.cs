namespace Raider.Core.Models;

/// <summary>
/// One self-contained piece of routine in-game work, such as claiming playtime rewards or collecting the Gem Mine.
/// </summary>
public sealed record Chore(
    string Id,
    string Name,
    ChoreType Type
);

/// <summary>
/// Types of chores supported by Raider.
/// </summary>
public enum ChoreType
{
    ClaimPlaytimeRewards,
    ClaimQuests,
    CollectInbox,
    BuyFreeMarketItems,
    CollectGemMine,
    ClanCheckIn
}