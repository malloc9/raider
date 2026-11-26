namespace Raider.Plugin.Chores;

using Raider.Plugin.Game;
using static Raider.Plugin.Game.Il2CppReflection;

/// <summary>
/// Buy free Market items: takes Market items whose price is zero; nothing that costs silver or gems.
/// <para>
/// Game side: the Market is <c>UserWrapper.MagicShop</c> (Client.Model.Gameplay.MagicShop.MagicShopWrapper);
/// <c>GetAvailableItems()</c> lists its MagicShopItems, each with a <c>Price</c> (SharedModel.Meta.Account.Resources);
/// <c>BuyMagicShopItemCmd(itemId)</c> buys one. (The game's "BlackMarket" is the player's item inventory, not the
/// Market.)
/// </para>
/// <para>
/// Spending guard: an item counts as free only if its price reads as empty with zero silver, zero gems and no
/// non-zero resource at all; anything unreadable, a missing (null) price included, counts as not free. The price is read again on the main thread
/// immediately before the buy command is created.
/// </para>
/// </summary>
internal sealed class BuyFreeMarketItemsChoreHandler : IChoreHandler
{
    private const string Buy = "Client.Model.Gameplay.MagicShop.Commands.BuyMagicShopItemCmd";

    public Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters) =>
        ChoreRun.ExecuteAsync("Buy free Market items", parameters, Plan);

    private static ChorePlan Plan(object userWrapper)
    {
        var market = GetRequired(userWrapper, "MagicShop");
        var items = Items(Call(market, "GetAvailableItems")).ToList();
        if (items.Count == 0)
        {
            return ChorePlan.Nothing("NoMarketItems");
        }

        var commands = new List<PlannedCommand>();
        var seen = new List<object>();
        foreach (var item in items)
        {
            var id = Convert.ToInt32(Get(item, "Id"));
            var type = Get(item, "TypeId")?.ToString();
            var price = Get(item, "Price");
            var free = IsFree(price);
            seen.Add(new { itemId = id, type, price = DescribePrice(price), free });
            if (free)
            {
                commands.Add(new PlannedCommand(Buy, new object?[] { id }, new { itemId = id, type }, () => RecheckFree(id)));
            }
        }

        var details = new { marketItems = seen };
        return commands.Count == 0 ? ChorePlan.Nothing("NoFreeItems", details) : new ChorePlan(commands, "", details);
    }

    /// <summary>Runs on the main thread right before buying: the item must still be there and still cost nothing.</summary>
    private static string? RecheckFree(int itemId)
    {
        var market = GameAccess.GetUserWrapper() is { } userWrapper ? Get(userWrapper, "MagicShop") : null;
        var item = market == null ? null : Call(market, "GetItem", itemId);
        if (item == null)
        {
            return "the item is no longer in the Market";
        }

        return IsFree(Get(item, "Price")) ? null : "the item is no longer free";
    }

    /// <summary>The price's non-zero resources, e.g. "Silver=25000", for the report.</summary>
    private static string DescribePrice(object? price)
    {
        if (price == null)
        {
            return "none";
        }

        try
        {
            var parts = Items(Get(price, "RawValues"))
                .Select(entry => (resource: Get(entry, "Key")?.ToString(), amount: Convert.ToDouble(Get(entry, "Value"))))
                .Where(p => p.amount != 0)
                .Select(p => $"{p.resource}={p.amount}");
            var text = string.Join(", ", parts);
            return text.Length == 0 ? "zero" : text;
        }
        catch (Exception ex)
        {
            return $"unreadable ({ex.Message})";
        }
    }

    private static bool IsFree(object? price)
    {
        // No price at all is unreadable, not free: a game update that moves the price must not make everything free
        if (price == null)
        {
            return false;
        }

        try
        {
            if (!Convert.ToBoolean(Call(price, "IsEmpty"))
                || Convert.ToDouble(Get(price, "Silver")) != 0
                || Convert.ToDouble(Get(price, "Gems")) != 0)
            {
                return false;
            }

            // Every resource the price lists must be zero
            return Items(Get(price, "RawValues")).All(entry => Convert.ToDouble(Get(entry, "Value")) == 0);
        }
        catch (Exception ex)
        {
            RaiderPlugin.Log.LogWarning($"Could not read a Market price, treating it as not free: {ex.Message}");
            return false;
        }
    }
}
