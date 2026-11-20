namespace Raider.Plugin.Chores;

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Raider.Plugin;

/// <summary>
/// Handles the Claim Quests chore execution.
/// Collects every completed quest reward on the Daily, Weekly, Monthly and Advanced tabs,
/// plus the Daily and Weekly progress chests.
/// </summary>
internal sealed class ClaimQuestsChoreHandler : IChoreHandler
{
    private readonly ManualLogSource _log;

    public ClaimQuestsChoreHandler()
    {
        _log = RaiderPlugin.Log;
    }

    public async Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        try
        {
            _log.LogInfo("Starting Claim Quests chore");

            // Step 1: Get all quest rewards
            var questRewards = await GetQuestRewardsAsync();
            if (questRewards == null || questRewards.Count == 0)
            {
                _log.LogInfo("Claim Quests: No quest rewards available - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoQuestRewards" });
            }

            _log.LogInfo($"Claim Quests: Found {questRewards.Count} quest reward(s)");

            // Step 2: Filter for completed quest rewards (ready to claim)
            var readyRewards = questRewards.Where(reward => reward.IsCompleted && !reward.IsClaimed).ToList();
            if (readyRewards.Count == 0)
            {
                _log.LogInfo("Claim Quests: No completed unclaimed quest rewards - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoCompletedRewards" });
            }

            _log.LogInfo($"Claim Quests: Found {readyRewards.Count} completed unclaimed reward(s) to claim");

            // Step 3: Claim each ready reward
            var claimedRewards = new List<object>();
            foreach (var reward in readyRewards)
            {
                var claimResult = await ClaimRewardAsync(reward);
                if (claimResult.Success)
                {
                    claimedRewards.Add(new
                    {
                        rewardId = reward.RewardId,
                        questType = reward.QuestType,
                        tab = reward.Tab,
                        rewards = claimResult.Rewards
                    });
                    _log.LogInfo($"Claim Quests: Claimed {reward.QuestType} on {reward.Tab} tab (ID: {reward.RewardId})");
                }
                else
                {
                    _log.LogWarning($"Claim Quests: Failed to claim {reward.QuestType} on {reward.Tab} tab (ID: {reward.RewardId}): {claimResult.ErrorMessage}");
                }
                
                await Task.Yield(); // Allow Unity main thread to process between claims
            }

            // Step 4: Also check and claim progress chests (Daily and Weekly)
            var progressChestResults = await ClaimProgressChestsAsync();
            claimedRewards.AddRange(progressChestResults);

            if (claimedRewards.Count == 0)
            {
                _log.LogWarning("Claim Quests: Attempted to claim rewards but none succeeded");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "CollectionFailed" });
            }

            _log.LogInfo($"Claim Quests: Successfully claimed {claimedRewards.Count} reward(s)");
            return new ChoreHandlerResult(
                true,
                null,
                new { outcome = "Success", claimed = claimedRewards, remaining = questRewards.Count - claimedRewards.Count });
        }
        catch (Exception ex)
        {
            _log.LogError($"Claim Quests chore failed with exception: {ex}");
            return new ChoreHandlerResult(false, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets all quest rewards from the game across all tabs.
    /// </summary>
    private async Task<List<QuestReward>?> GetQuestRewardsAsync()
    {
        await Task.Yield(); // Allow Unity main thread to process

        try
        {
            // In the real implementation, this would access the game's quest system via reflection
            // Example:
            // var questController = QuestController.Instance;
            // if (questController == null) return null;
            // return questController.GetAllRewards().Select(ConvertToQuestReward).ToList();

            // For now, return placeholder data - this needs actual game API integration
            _log.LogWarning("GetQuestRewardsAsync: Using placeholder - needs actual game API integration");

            return new List<QuestReward>();
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting quest rewards: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Claims a specific quest reward.
    /// </summary>
    private async Task<ClaimRewardResult> ClaimRewardAsync(QuestReward reward)
    {
        await Task.Yield();

        try
        {
            // In the real implementation, this would call the game's claim method
            // Example:
            // var questController = QuestController.Instance;
            // var result = await questController.ClaimQuestRewardAsync(reward.RewardId);
            // return new ClaimRewardResult(result.Success, result.Rewards, result.ErrorMessage);

            // Placeholder - needs actual game API integration
            _log.LogWarning($"ClaimRewardAsync: Using placeholder for reward {reward.RewardId} - needs actual game API integration");
            return new ClaimRewardResult(false, null, "Not implemented - needs game API integration");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error claiming quest reward {reward.RewardId}: {ex.Message}");
            return new ClaimRewardResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Claims Daily and Weekly progress chests if available.
    /// </summary>
    private async Task<List<object>> ClaimProgressChestsAsync()
    {
        await Task.Yield();

        var claimedChests = new List<object>();

        try
        {
            // In the real implementation, this would check and claim progress chests
            // Example:
            // var questController = QuestController.Instance;
            // if (questController.CanClaimDailyProgressChest)
            // {
            //     var result = await questController.ClaimDailyProgressChestAsync();
            //     if (result.Success) claimedChests.Add(...);
            // }
            // Same for Weekly progress chest

            _log.LogWarning("ClaimProgressChestsAsync: Using placeholder - needs actual game API integration");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error claiming progress chests: {ex.Message}");
        }

        return claimedChests;
    }

    /// <summary>
    /// Represents a quest reward.
    /// </summary>
    private sealed class QuestReward
    {
        public string RewardId { get; init; } = string.Empty;
        public string QuestType { get; init; } = string.Empty; // e.g., "Daily", "Weekly", "Monthly", "Advanced"
        public string Tab { get; init; } = string.Empty; // e.g., "Daily", "Weekly", "Monthly", "Advanced"
        public bool IsCompleted { get; init; }
        public bool IsClaimed { get; init; }
        public object? RawData { get; init; }
    }

    /// <summary>
    /// Result of claiming a quest reward.
    /// </summary>
    private sealed record ClaimRewardResult(
        bool Success,
        object? Rewards = null,
        string? ErrorMessage = null);
}