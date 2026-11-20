namespace Raider.Plugin;

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using System.Diagnostics;
using HarmonyLib;
using Raider.Plugin.Game;
using Raider.Plugin.Ipc;
using Raider.Shared;

/// <summary>
/// Main Raider plugin entry point for BepInEx 6 (IL2CPP).
/// </summary>
[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class RaiderPlugin : BasePlugin
{
    /// <summary>Plugin-wide logger, set in <see cref="Load"/>.</summary>
    internal static new ManualLogSource Log { get; private set; } = null!;
    internal static RaiderPlugin Instance { get; private set; } = null!;
    internal ChoreExecutor ChoreExecutor { get; private set; } = null!;
    internal PluginServer Server { get; private set; } = null!;
    internal ConfigEntry<string> HealthTokenConfig { get; private set; } = null!;

    private readonly Harmony _harmony = new(PluginInfo.PLUGIN_GUID);

    public override void Load()
    {
        Log = base.Log;
        Instance = this;

        // BepInEx/config/com.malloc9.raider.cfg, written by the desktop app's installer
        HealthTokenConfig = Config.Bind("Raider.Plugin", "HealthToken", "", "Per-install secret token every request to the plugin must carry");

        // Initialize game access
        GameAccess.Initialize(Log);
        MainThread.Start(this, Log);

        ChoreExecutor = new ChoreExecutor();
        StartServer();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Unregister();

        Log.LogInfo($"Raider plugin {PluginInfo.PLUGIN_VERSION} loaded");
    }

    public override bool Unload()
    {
        Unregister();
        Server?.Stop();
        _harmony.UnpatchSelf();
        Log.LogInfo("Raider plugin unloaded");
        return true;
    }

    internal void ReloadHealthConfig()
    {
        Unregister();
        Server?.Stop();
        StartServer();
    }

    /// <summary>
    /// Starts the server on a free port and registers this Instance so Raider can find it.
    /// </summary>
    private void StartServer()
    {
        Server = new PluginServer(HealthTokenConfig.Value, Log, ChoreExecutor.HandleRequestAsync);
        Server.Start();
        if (Server.Port == 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            var registration = new InstanceRegistration(process.Id, Server.Port, process.StartTime.ToUniversalTime());
            registration.WriteTo(InstanceRegistration.DefaultDirectory);
            Log.LogInfo($"Registered Instance {registration.Pid} on port {registration.Port}");
        }
        catch (Exception ex)
        {
            Log.LogError($"Failed to register Instance: {ex.Message}");
        }
    }

    private static void Unregister() =>
        InstanceRegistration.Delete(InstanceRegistration.DefaultDirectory, Environment.ProcessId);
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
