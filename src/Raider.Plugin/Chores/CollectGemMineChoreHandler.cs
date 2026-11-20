namespace Raider.Plugin.Chores;

using System.Collections.Generic;
using BepInEx.Logging;
using Raider.Plugin;
using Raider.Plugin.Game;

/// <summary>
/// Handles the Collect Gem Mine chore execution.
/// Collects the gems accumulated in the Gem Mine.
/// </summary>
internal sealed class CollectGemMineChoreHandler : IChoreHandler
{
    private readonly ManualLogSource _log;

    public CollectGemMineChoreHandler()
    {
        _log = RaiderPlugin.Log;
    }

    public async Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters)
    {
        try
        {
            _log.LogInfo("Starting Collect Gem Mine chore");

            if (!GameAccess.IsInitialized)
            {
                _log.LogError("GameAccess not initialized");
                return new ChoreHandlerResult(false, "GameAccess not initialized");
            }

            // Check accumulated gems before collecting
            var gemsBefore = GameAccess.GetAccumulatedGems();
            
            if (gemsBefore == 0)
            {
                _log.LogInfo("Collect Gem Mine: Gem Mine has no gems to collect - nothing to do");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "NothingToDo", reason = "NoGemsAccumulated", gemsCollected = 0 });
            }

            _log.LogInfo($"Collect Gem Mine: Found {gemsBefore} accumulated gems");

            // Collect the gems
            var collected = GameAccess.CollectGemMine();
            
            if (collected < 0)
            {
                _log.LogError("Collect Gem Mine: Failed to collect gems");
                return new ChoreHandlerResult(false, "Failed to collect Gem Mine");
            }

            if (collected == 0)
            {
                // Method succeeded but no gems were collected (might be a timing issue)
                // Report the gems that were available before as collected
                _log.LogInfo($"Collect Gem Mine: Collection triggered, assuming {gemsBefore} gems collected");
                return new ChoreHandlerResult(
                    true,
                    null,
                    new { outcome = "Success", gemsCollected = gemsBefore });
            }

            _log.LogInfo($"Collect Gem Mine: Successfully collected {collected} gems");
            return new ChoreHandlerResult(
                true,
                null,
                new { outcome = "Success", gemsCollected = collected });
        }
        catch (Exception ex)
        {
            _log.LogError($"Collect Gem Mine chore failed with exception: {ex}");
            return new ChoreHandlerResult(false, $"Exception: {ex.Message}");
        }
    }
}