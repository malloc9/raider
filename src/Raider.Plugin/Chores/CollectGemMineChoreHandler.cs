namespace Raider.Plugin.Chores;

using BepInEx.Logging;
using Raider.Plugin.Game;

/// <summary>
/// Handles the Collect Gem Mine chore execution.
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

            var collected = GameAccess.CollectGemMine();
            
            if (collected >= 0)
            {
                _log.LogInfo($"Collect Gem Mine: Successfully collected {collected} gems");
                return new ChoreHandlerResult(
                    true,
                    null,
                    collected);
            }
            else
            {
                _log.LogError("Failed to collect Gem Mine");
                return new ChoreHandlerResult(false, "Failed to collect Gem Mine");
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Collect Gem Mine chore failed with exception: {ex}");
            return new ChoreHandlerResult(false, $"Exception: {ex.Message}");
        }
    }
}