namespace Raider.Plugin;

using BepInEx;
using BepInEx.Logging;
using System.Reflection;

/// <summary>
/// Main Raider plugin entry point for BepInEx.
/// </summary>
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class RaiderPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; } = null!;
    internal static RaiderPlugin Instance { get; private set; } = null!;
    internal ChoreExecutor ChoreExecutor { get; private set; } = null!;

    private void Awake()
    {
        Log = Logger;
        Instance = this;
        
        // Initialize chore executor
        ChoreExecutor = new ChoreExecutor();
        
        // Start the IPC server for communication with the desktop app
        _ = ChoreExecutor.StartIpcServerAsync();
        
        Log.LogInfo($"Raider plugin {PluginInfo.PLUGIN_VERSION} loaded");
    }

    private void OnDestroy()
    {
        ChoreExecutor?.StopIpcServer();
        Log.LogInfo("Raider plugin unloaded");
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