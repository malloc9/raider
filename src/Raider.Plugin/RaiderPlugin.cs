namespace Raider.Plugin;

using System.Net;
using System.Text;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Raider.Plugin.Chores;
using Raider.Plugin.Game;
using Raider.Plugin.Ipc;

/// <summary>
/// Main Raider plugin entry point for BepInEx 6.
/// </summary>
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class RaiderPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static RaiderPlugin Instance { get; private set; } = null!;
    internal ChoreExecutor ChoreExecutor { get; private set; } = null!;
    internal HealthServer HealthServer { get; private set; } = null!;
    internal ConfigEntry<string> HealthTokenConfig { get; private set; } = null!;
    internal ConfigEntry<int> HealthPortConfig { get; private set; } = null!;

    private readonly Harmony _harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Log = Logger;
        Instance = this;

        // Read config for health endpoint
        HealthTokenConfig = Config.Bind("Raider.Plugin", "HealthToken", "", "Per-install secret token for health endpoint authentication");
        HealthPortConfig = Config.Bind("Raider.Plugin", "HealthPort", 51837, "Health endpoint port (bound to 127.0.0.1)");

        // Initialize game access
        GameAccess.Initialize(Log);
        
        // Initialize chore handlers
        GemMineChore.Initialize(Log, _harmony);
        
        // Start chore executor (for desktop app IPC)
        ChoreExecutor = new ChoreExecutor();
        _ = ChoreExecutor.StartIpcServerAsync();
        
        // Start health server
        HealthServer = new HealthServer(HealthTokenConfig.Value, HealthPortConfig.Value, Log);
        HealthServer.Start();
        
        Log.LogInfo($"Raider plugin {PluginInfo.PLUGIN_VERSION} loaded");
    }

    private void OnDestroy()
    {
        ChoreExecutor?.StopIpcServer();
        HealthServer?.Stop();
        _harmony.UnpatchAll(PluginInfo.PLUGIN_GUID);
        Log.LogInfo("Raider plugin unloaded");
    }
    
    internal void ReloadHealthConfig()
    {
        HealthServer?.Stop();
        HealthServer = new HealthServer(HealthTokenConfig.Value, HealthPortConfig.Value, Log);
        HealthServer.Start();
    }
}

/// <summary>
/// Plugin metadata constants.
/// </summary>
internal static class PluginInfo
{
    public const string PLUGIN_GUID = "com.malloc9.raider";
    public const string PLUGIN_NAME = "Raider";
    public const string PLUGIN_VERSION = "1.0.0";
}