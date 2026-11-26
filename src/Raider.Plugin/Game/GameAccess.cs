using System.Reflection;
using BepInEx.Logging;
using Il2CppInterop.Runtime;

namespace Raider.Plugin.Game;

/// <summary>
/// Reads Raid's own model through its Il2CppInterop proxy assemblies (BepInEx\interop), by reflection so the
/// plugin builds without a local game install.
/// <para>
/// Raid has no Assembly-CSharp: its code is split across Unity.App, Unity.Model, Unity.SharedModel, Unity.ViewModel
/// and others. The root of the game's state is <c>Client.App.SingleInstance&lt;Client.Model.AppModel&gt;.Instance</c>.
/// </para>
/// <para>
/// Called from the plugin server's thread pool, so every read first attaches the thread to the IL2CPP runtime.
/// Only reads are safe this way; anything that changes game state must run on Unity's main thread.
/// </para>
/// </summary>
public static class GameAccess
{
    private const string AppModelTypeName = "Client.Model.AppModel";
    private const string SingleInstanceTypeName = "Client.App.SingleInstance`1";

    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly object InitLock = new();
    private static ManualLogSource? _logger;
    private static bool _initialized;

    private static PropertyInfo? _appModelInstance;
    private static PropertyInfo? _userId;
    private static PropertyInfo? _clientVersion;

    public static void Initialize(ManualLogSource logger)
    {
        _logger = logger;
        TryInitialize();
    }

    /// <summary>True once Raid's AppModel type has been found; the AppModel itself may still be missing (game loading).</summary>
    public static bool IsInitialized => TryInitialize();

    /// <summary>
    /// The game's AppModel, or null while the game is still loading.
    /// </summary>
    public static object? GetAppModel()
    {
        if (!TryInitialize())
        {
            return null;
        }

        try
        {
            AttachThread();
            return _appModelInstance!.GetValue(null);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"AppModel not available yet: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// The logged-in player's Raid user ID, or null while no player is logged in.
    /// </summary>
    public static long? GetUserId()
    {
        var appModel = GetAppModel();
        if (appModel == null || _userId == null)
        {
            return null;
        }

        try
        {
            var id = Convert.ToInt64(_userId.GetValue(appModel));
            return id > 0 ? id : null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Could not read AppModel.UserId: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether a player is logged in and the game's model is up.
    /// </summary>
    public static bool IsInGame() => GetUserId() != null;

    /// <summary>
    /// The game client's version (AppModel.ClientVersion), falling back to Unity's Application.version.
    /// </summary>
    public static string GetGameBuild()
    {
        var appModel = GetAppModel();
        if (appModel != null && _clientVersion != null)
        {
            try
            {
                var version = FormatAppVersion(_clientVersion.GetValue(appModel));
                if (!string.IsNullOrEmpty(version))
                {
                    return version;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Could not read AppModel.ClientVersion: {ex.GetBaseException().Message}");
            }
        }

        return GetUnityApplicationVersion() ?? "unknown";
    }

    /// <summary>
    /// The logged-in player's game state (AppModel._userWrapper, a Client.Model.Guard.UserWrapper), or null while no
    /// player is logged in. Read it on the main thread (<see cref="MainThread"/>) when acting on it.
    /// </summary>
    public static object? GetUserWrapper()
    {
        var appModel = GetAppModel();
        return appModel == null ? null : Il2CppReflection.Get(appModel, "_userWrapper");
    }

    private static bool TryInitialize()
    {
        if (_initialized)
        {
            return true;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return true;
            }

            try
            {
                var appModelType = FindGameType(AppModelTypeName);
                var singleInstanceType = FindGameType(SingleInstanceTypeName);
                if (appModelType == null || singleInstanceType == null)
                {
                    _logger?.LogWarning($"Raid types not found yet (AppModel: {appModelType != null}, SingleInstance: {singleInstanceType != null})");
                    return false;
                }

                var appModelHolder = singleInstanceType.MakeGenericType(appModelType);
                _appModelInstance = appModelHolder.GetProperty("Instance", AnyStatic)
                    ?? appModelHolder.GetProperty("_instance", AnyStatic);
                if (_appModelInstance == null)
                {
                    _logger?.LogError("SingleInstance<AppModel> has no Instance property");
                    return false;
                }

                _userId = appModelType.GetProperty("UserId", AnyInstance);
                _clientVersion = appModelType.GetProperty("ClientVersion", AnyInstance);

                _initialized = true;
                _logger?.LogInfo(
                    $"GameAccess hooked into {appModelType.Assembly.GetName().Name}: AppModel " +
                    $"(UserId: {_userId != null}, ClientVersion: {_clientVersion != null})");
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Failed to initialize GameAccess: {ex}");
                return false;
            }
        }
    }

    /// <summary>
    /// Finds a type in Raid's interop assemblies (the ones BepInEx generated from the game, named Unity.*).
    /// </summary>
    internal static Type? FindGameType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (!name.StartsWith("Unity.", StringComparison.Ordinal))
            {
                continue;
            }

            var type = assembly.GetType(fullName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        // Interop assemblies load lazily; Raid keeps its app layer in these
        foreach (var assemblyName in new[] { "Unity.Model", "Unity.App", "Unity.SharedModel" })
        {
            try
            {
                var type = Assembly.Load(assemblyName).GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                _logger?.LogDebug($"Could not load {assemblyName}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// ClientVersion is a Client.App.Settings.AppVersion; its interop proxy's ToString() gives only the type name.
    /// </summary>
    private static string? FormatAppVersion(object? appVersion)
    {
        if (appVersion == null)
        {
            return null;
        }

        var type = appVersion.GetType();
        object? Part(string name) => type.GetProperty(name, AnyInstance)?.GetValue(appVersion);
        var (major, minor, build) = (Part("Major"), Part("Minor"), Part("Build"));
        return major != null && minor != null && build != null ? $"{major}.{minor}.{build}" : null;
    }

    private static string? GetUnityApplicationVersion()
    {
        try
        {
            AttachThread();
            var application = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Application");
            return application?.GetProperty("version", AnyStatic)?.GetValue(null)?.ToString();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug($"Could not read Application.version: {ex.GetBaseException().Message}");
            return null;
        }
    }

    /// <summary>
    /// IL2CPP objects may only be touched from threads the IL2CPP runtime knows about.
    /// </summary>
    private static void AttachThread() => IL2CPP.il2cpp_thread_attach(IL2CPP.il2cpp_domain_get());
}
