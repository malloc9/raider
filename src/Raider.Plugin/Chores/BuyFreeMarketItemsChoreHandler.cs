namespace Raider.Plugin.Chores;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Raider.Plugin;

/// <summary>
/// Handles the Buy Free Market Items chore execution.
/// </summary>
internal sealed class BuyFreeMarketItemsChoreHandler : IChoreHandler
{
    private readonly ManualLogSource _log;

    public BuyFreeMarketItemsChoreHandler()
    {
        _log = RaiderPlugin.Log;
    }

    public async Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        try
        {
            _log.LogInfo("Starting Buy Free Market Items chore");

            // Step 1: Get the market items
            var marketItems = await GetMarketItemsAsync();
            if (marketItems == null || marketItems.Count == 0)
            {
                _log.LogInfo("Buy Free Market Items: No market items available - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoMarketItems" });
            }

            _log.LogInfo($"Buy Free Market Items: Found {marketItems.Count} market items");

            // Step 2: Filter for free items (price == 0)
            var freeItems = marketItems.Where(item => item.Price == 0).ToList();
            if (freeItems.Count == 0)
            {
                _log.LogInfo("Buy Free Market Items: No free items found - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoFreeItems" });
            }

            _log.LogInfo($"Buy Free Market Items: Found {freeItems.Count} free items to take");

            // Step 3: Take each free item
            var takenItems = new List<object>();
            foreach (var item in freeItems)
            {
                var takeResult = await TakeMarketItemAsync(item);
                if (takeResult.Success)
                {
                    takenItems.Add(new
                    {
                        itemId = item.ItemId,
                        itemName = item.ItemName,
                        quantity = item.Quantity
                    });
                    _log.LogInfo($"Buy Free Market Items: Took {item.Quantity}x {item.ItemName} (ID: {item.ItemId})");
                }
                else
                {
                    _log.LogWarning($"Buy Free Market Items: Failed to take {item.ItemName} (ID: {item.ItemId}): {takeResult.ErrorMessage}");
                }
            }

            if (takenItems.Count == 0)
            {
                _log.LogWarning("Buy Free Market Items: Failed to take any free items");
                return new ChoreHandlerResult(
                    false,
                    "Failed to take any free market items");
            }

            _log.LogInfo($"Buy Free Market Items: Successfully took {takenItems.Count} free item(s)");
            return new ChoreHandlerResult(
                true,
                null,
                new { outcome = "Success", taken = takenItems });
        }
        catch (Exception ex)
        {
            _log.LogError($"Buy Free Market Items chore failed with exception: {ex}");
            return new ChoreHandlerResult(false, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all available market items from the game.
    /// </summary>
    private async Task<List<MarketItem>?> GetMarketItemsAsync()
    {
        await Task.Yield(); // Allow Unity main thread to process

        try
        {
            // In the real implementation, this would access the game's market system via reflection
            // Example:
            // var marketController = MarketController.Instance;
            // if (marketController == null) return null;
            // return marketController.CurrentMarketItems.Select(ConvertToMarketItem).ToList();

            // For now, return placeholder data - this needs actual game API integration
            _log.LogWarning("GetMarketItemsAsync: Using placeholder - needs actual game API integration");
            
            // Simulate finding some market items for testing
            // In reality, this would query the game's actual market
            return new List<MarketItem>();
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting market items: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Takes a specific market item (purchases it for free).
    /// </summary>
    private async Task<TakeItemResult> TakeMarketItemAsync(MarketItem item)
    {
        await Task.Yield();

        try
        {
            // In the real implementation, this would call the game's market purchase method
            // Example:
            // var marketController = MarketController.Instance;
            // var result = await marketController.PurchaseItemAsync(item.ItemId);
            // return new TakeItemResult(result.Success, result.ErrorMessage);

            // Placeholder - needs actual game API integration
            _log.LogWarning($"TakeMarketItemAsync: Using placeholder for item {item.ItemId} - needs actual game API integration");
            return new TakeItemResult(false, "Not implemented - needs game API integration");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error taking market item {item.ItemId}: {ex.Message}");
            return new TakeItemResult(false, ex.Message);
        }
    }

    /// <summary>
    /// Represents a market item.
    /// </summary>
    private sealed class MarketItem
    {
        public string ItemId { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public int Quantity { get; init; }
        public int Price { get; init; } // 0 = free, >0 = costs silver or gems
        public string CurrencyType { get; init; } = string.Empty; // "Silver", "Gems", "Free"
    }

    /// <summary>
    /// Result of taking a market item.
    /// </summary>
    private sealed record TakeItemResult(bool Success, string? ErrorMessage = null);
}