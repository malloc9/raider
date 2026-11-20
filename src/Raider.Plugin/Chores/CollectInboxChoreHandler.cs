namespace Raider.Plugin.Chores;

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Raider.Plugin;

/// <summary>
/// Handles the Collect Inbox chore execution.
/// Collects inbox items that need no choice; leaves choice packs and items requiring a pick untouched.
/// </summary>
internal sealed class CollectInboxChoreHandler : IChoreHandler
{
    private readonly ManualLogSource _log;

    public CollectInboxChoreHandler()
    {
        _log = RaiderPlugin.Log;
    }

    public async Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        try
        {
            _log.LogInfo("Starting Collect Inbox chore");

            // Step 1: Get all inbox items
            var inboxItems = await GetInboxItemsAsync();
            if (inboxItems == null || inboxItems.Count == 0)
            {
                _log.LogInfo("Collect Inbox: Inbox is empty - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "InboxEmpty" });
            }

            _log.LogInfo($"Collect Inbox: Found {inboxItems.Count} inbox item(s)");

            // Step 2: Filter items that can be auto-collected (no choice required)
            var collectibleItems = inboxItems
                .Where(item => CanAutoCollect(item))
                .ToList();

            if (collectibleItems.Count == 0)
            {
                _log.LogInfo("Collect Inbox: No auto-collectible items found (only choice packs or items requiring a pick) - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoAutoCollectibleItems" });
            }

            _log.LogInfo($"Collect Inbox: {collectibleItems.Count} item(s) can be auto-collected");

            // Step 3: Collect the auto-collectible items
            var collectedItems = new List<object>();
            foreach (var item in collectibleItems)
            {
                var result = await CollectItemAsync(item);
                if (result.Success)
                {
                    collectedItems.Add(new 
                    { 
                        itemId = result.ItemId, 
                        itemType = result.ItemType, 
                        rewards = result.Rewards 
                    });
                    _log.LogInfo($"Collect Inbox: Collected item {result.ItemId} ({result.ItemType})");
                }
                else
                {
                    _log.LogWarning($"Collect Inbox: Failed to collect item {item.ItemId}: {result.ErrorMessage}");
                }
                
                await Task.Yield(); // Allow Unity main thread to process between collections
            }

            if (collectedItems.Count == 0)
            {
                _log.LogWarning("Collect Inbox: Attempted to collect items but none succeeded");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "CollectionFailed" });
            }

            _log.LogInfo($"Collect Inbox: Successfully collected {collectedItems.Count} item(s)");
            return new ChoreHandlerResult(
                true,
                null,
                new { outcome = "Success", collected = collectedItems, leftInInbox = inboxItems.Count - collectedItems.Count });
        }
        catch (Exception ex)
        {
            _log.LogError($"Collect Inbox chore failed with exception: {ex}");
            return new ChoreHandlerResult(false, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all inbox items from the game.
    /// </summary>
    private async Task<List<InboxItem>?> GetInboxItemsAsync()
    {
        await Task.Yield(); // Allow Unity main thread to process
        
        try
        {
            // In the real implementation, this would access the game's inbox system
            // Example: var inboxController = InboxController.Instance;
            // return inboxController.GetAllItems();
            
            // For now, return placeholder data
            _log.LogWarning("GetInboxItemsAsync: Using placeholder - needs actual game API integration");
            return new List<InboxItem>(); // Placeholder - empty inbox
        }
        catch (Exception ex)
        {
            _log.LogError($"GetInboxItemsAsync failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Determines if an item can be auto-collected (needs no choice).
    /// Choice packs and items requiring a pick return false.
    /// </summary>
    private bool CanAutoCollect(InboxItem item)
    {
        // Items that require a choice/pick cannot be auto-collected
        // This includes choice packs, hero selection items, etc.
        if (item.RequiresChoice)
        {
            _log.LogDebug($"Item {item.ItemId} requires choice - leaving in inbox");
            return false;
        }

        // Check if item type is a choice pack
        if (IsChoicePack(item.ItemType))
        {
            _log.LogDebug($"Item {item.ItemId} is a choice pack ({item.ItemType}) - leaving in inbox");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if the item type is a choice pack.
    /// </summary>
    private bool IsChoicePack(string itemType)
    {
        // Game-specific item type identifiers for choice packs
        var choicePackTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ChoicePack",
            "HeroChoicePack",
            "ArtifactChoicePack",
            "ShardChoicePack",
            "SkinChoicePack",
            "Selector"
        };

        return choicePackTypes.Contains(itemType);
    }

    /// <summary>
    /// Collects a single inbox item.
    /// </summary>
    private async Task<CollectItemResult> CollectItemAsync(InboxItem item)
    {
        await Task.Yield();
        
        try
        {
            // In the real implementation, this would call the game's collect method
            // Example: var result = await InboxController.Instance.CollectItemAsync(item.ItemId);
            
            // Placeholder implementation
            _log.LogWarning("CollectItemAsync: Using placeholder - needs actual game API integration");
            
            // Simulate success
            return new CollectItemResult(true, item.ItemId, item.ItemType, new { silver = 1000 }, null);
        }
        catch (Exception ex)
        {
            _log.LogError($"CollectItemAsync failed for item {item.ItemId}: {ex.Message}");
            return new CollectItemResult(false, item.ItemId, item.ItemType, null, ex.Message);
        }
    }

    /// <summary>
    /// Represents an inbox item.
    /// </summary>
    private sealed class InboxItem
    {
        public string ItemId { get; set; } = string.Empty;
        public string ItemType { get; set; } = string.Empty;
        public bool RequiresChoice { get; set; }
        public object? RawData { get; set; }
    }

    /// <summary>
    /// Result of collecting a single item.
    /// </summary>
    private sealed record CollectItemResult(
        bool Success,
        string ItemId,
        string ItemType,
        object? Rewards,
        string? ErrorMessage);
}