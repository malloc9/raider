using BepInEx.Logging;
using HarmonyLib;
using Raider.Plugin.Game;
using Raider.Plugin.Ipc;
using System;
using System.Reflection;

namespace Raider.Plugin.Chores;

/// <summary>
/// Handles the Collect Gem Mine chore.
/// </summary>
public static class GemMineChore
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger, Harmony harmony)
    {
        _logger = logger;
        _harmony = harmony;

        // Register the chore handler with the IPC server
        IpcServer.RegisterChoreHandler("CollectGemMine", HandleCollectGemMine);
        
        _logger.LogInfo("GemMineChore initialized");
    }

    private static ChoreResult HandleCollectGemMine()
    {
        _logger?.LogInfo("Handling CollectGemMine chore");

        if (!GameAccess.IsInitialized)
        {
            _logger?.LogError("GameAccess not initialized");
            return new ChoreResult(false, "GameAccess not initialized");
        }

        try
        {
            var collected = GameAccess.CollectGemMine();
            
            if (collected >= 0)
            {
                return new ChoreResult(true, null, collected);
            }
            else
            {
                return new ChoreResult(false, "Failed to collect Gem Mine");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Exception in CollectGemMine: {ex}");
            return new ChoreResult(false, $"Exception: {ex.Message}");
        }
    }
}

/// <summary>
/// Result of a chore execution in the plugin.
/// </summary>
public readonly record struct ChoreResult(
    bool Success,
    string? ErrorMessage,
    int GemsCollected = 0
);