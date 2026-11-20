using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Raider.Plugin.Chores;
using Raider.Plugin.Game;
using Raider.Plugin.Ipc;
using System.Reflection;

namespace Raider.Plugin;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    private readonly Harmony _harmony = new(PluginInfo.PLUGIN_GUID);

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

        // Initialize game access
        GameAccess.Initialize(Logger);
        
        // Initialize chore handlers
        GemMineChore.Initialize(Logger, _harmony);
        
        // Start IPC server
        IpcServer.Start(Logger);
        
        Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} initialized!");
    }

    private void OnDestroy()
    {
        IpcServer.Stop();
        _harmony.UnpatchAll(PluginInfo.PLUGIN_GUID);
    }
}

internal static class PluginInfo
{
    public const string PLUGIN_GUID = "com.raider.plugin";
    public const string PLUGIN_NAME = "Raider Plugin";
    public const string PLUGIN_VERSION = "1.0.0";
}