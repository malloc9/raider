namespace Raider.Plugin.Chores;

using BepInEx.Logging;
using HarmonyLib;
using Raider.Plugin.Game;

/// <summary>
/// Handles the Collect Gem Mine chore via Harmony patches.
/// This is the legacy patch-based approach; the new HTTP-based handler is CollectGemMineChoreHandler.
/// </summary>
public static class GemMineChore
{
    private static ManualLogSource? _logger;
    private static Harmony? _harmony;

    public static void Initialize(ManualLogSource logger, Harmony harmony)
    {
        _logger = logger;
        _harmony = harmony;

        // Harmony patches for Gem Mine would go here
        // For now, the HTTP-based CollectGemMineChoreHandler handles this chore
        
        _logger.LogInfo("GemMineChore initialized (legacy Harmony patches - HTTP handler is primary)");
    }
}

/// <summary>
/// Result of a chore execution in the plugin (legacy).
/// </summary>
public readonly record struct ChoreResult(
    bool Success,
    string? ErrorMessage,
    int GemsCollected = 0
);