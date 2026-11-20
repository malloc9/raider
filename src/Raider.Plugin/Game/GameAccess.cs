using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace Raider.Plugin.Game;

/// <summary>
/// Provides access to the game's internal classes and methods via reflection.
/// </summary>
public static class GameAccess
{
    private static ManualLogSource? _logger;
    private static bool _initialized;

    // Game assembly references (populated at runtime)
    private static Assembly? _assemblyCSharp;
    private static Assembly? _unityEngine;

    // Cached types for game state detection
    private static Type? _gameStateType;
    private static Type? _playerDataType;
    private static Type? _uiManagerType;
    private static Type? _gemMineType;
    private static Type? _versionType;

    // Cached methods/properties
    private static PropertyInfo? _isInGameProperty;
    private static MethodInfo? _getGameBuildMethod;
    private static PropertyInfo? _gameBuildProperty;
    private static MethodInfo? _collectGemMineMethod;
    private static PropertyInfo? _gemMineGemsProperty;
    private static MethodInfo? _getPlayerDataMethod;
    private static MethodInfo? _getGemMineControllerMethod;

    public static void Initialize(ManualLogSource logger)
    {
        if (_initialized) return;
        
        _logger = logger;
        
        try
        {
            // Find the game assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    _assemblyCSharp = assembly;
                    _logger.LogDebug("Found Assembly-CSharp");
                }
                else if (assembly.GetName().Name == "UnityEngine.CoreModule")
                {
                    _unityEngine = assembly;
                    _logger.LogDebug("Found UnityEngine.CoreModule");
                }
            }

            if (_assemblyCSharp == null)
            {
                _logger.LogError("Assembly-CSharp not found!");
                return;
            }

            CacheTypesAndMethods();
            _initialized = true;
            _logger.LogInfo("GameAccess initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize GameAccess: {ex}");
        }
    }

    private static void CacheTypesAndMethods()
    {
        if (_assemblyCSharp == null) return;

        // Try to find game state / version types
        _gameStateType = FindType("GameState", "GameManager", "GameController", "AppState");
        _versionType = FindType("GameVersion", "VersionInfo", "BuildInfo", "AppVersion");
        _playerDataType = FindType("PlayerData", "PlayerModel", "AccountData");
        _uiManagerType = FindType("UIManager", "WindowManager", "ViewManager");
        _gemMineType = FindType("GemMineController", "GemMineView", "GemMineModel", "GemMineData");

        // Try to find IsInGame property
        if (_gameStateType != null)
        {
            _isInGameProperty = _gameStateType.GetProperty("IsInGame",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gameStateType.GetProperty("InGame",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gameStateType.GetProperty("IsPlaying",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        // Try to find game build/version
        if (_versionType != null)
        {
            _gameBuildProperty = _versionType.GetProperty("Version",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _versionType.GetProperty("GameVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _versionType.GetProperty("BuildVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            _getGameBuildMethod = _versionType.GetMethod("GetVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }
        else if (_gameStateType != null)
        {
            _gameBuildProperty = _gameStateType.GetProperty("Version",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gameStateType.GetProperty("GameVersion",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        // Gem Mine methods
        if (_gemMineType != null)
        {
            _collectGemMineMethod = _gemMineType.GetMethod("Collect", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetMethod("CollectGems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetMethod("OnCollectClick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            _gemMineGemsProperty = _gemMineType.GetProperty("Gems", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetProperty("AccumulatedGems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetProperty("AvailableGems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetProperty("CurrentGems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        if (_playerDataType != null)
        {
            _getPlayerDataMethod = _playerDataType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetGetMethod(true)
                ?? _playerDataType.GetMethod("GetInstance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        }

        if (_uiManagerType != null)
        {
            _getGemMineControllerMethod = _uiManagerType.GetMethod("GetGemMineController",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _uiManagerType.GetMethod("GetWindow",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        }

        _logger?.LogDebug($"Cached types: GameState={_gameStateType?.Name}, Version={_versionType?.Name}, PlayerData={_playerDataType?.Name}, UIManager={_uiManagerType?.Name}, GemMine={_gemMineType?.Name}");
        _logger?.LogDebug($"Cached props: IsInGame={_isInGameProperty?.Name}, GameBuild={_gameBuildProperty?.Name}");
    }

    private static Type? FindType(params string[] possibleNames)
    {
        if (_assemblyCSharp == null) return null;

        foreach (var name in possibleNames)
        {
            var type = _assemblyCSharp.GetType(name);
            if (type != null)
            {
                _logger?.LogDebug($"Found type: {name}");
                return type;
            }

            // Try with namespace
            type = _assemblyCSharp.GetType($"Com.Plarium.Raid.{name}");
            if (type != null)
            {
                _logger?.LogDebug($"Found type with namespace: Com.Plarium.Raid.{name}");
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the game is currently in-game (not in menus, loading, etc.).
    /// </summary>
    public static bool IsInGame()
    {
        try
        {
            if (_isInGameProperty != null && _gameStateType != null)
            {
                var instance = GetGameStateInstance();
                if (instance != null)
                {
                    var value = _isInGameProperty.GetValue(instance);
                    if (value is bool inGame)
                    {
                        return inGame;
                    }
                }
            }

            // Fallback: check if player data exists (logged in)
            if (_playerDataType != null && _getPlayerDataMethod != null)
            {
                var playerData = _getPlayerDataMethod.Invoke(null, null);
                if (playerData != null)
                {
                    var idProperty = _playerDataType.GetProperty("Id",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                        ?? _playerDataType.GetProperty("AccountId",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (idProperty != null)
                    {
                        var idValue = idProperty.GetValue(playerData);
                        if (idValue != null && !string.IsNullOrEmpty(idValue.ToString()))
                        {
                            return true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error checking IsInGame: {ex}");
        }

        return false;
    }

    /// <summary>
    /// Gets the current game build version.
    /// </summary>
    public static string GetGameBuild()
    {
        try
        {
            if (_gameBuildProperty != null)
            {
                var instance = _versionType != null ? GetVersionInstance() : GetGameStateInstance();
                if (instance != null)
                {
                    var value = _gameBuildProperty.GetValue(instance);
                    if (value is string version && !string.IsNullOrEmpty(version))
                    {
                        return version;
                    }
                }
            }

            if (_getGameBuildMethod != null)
            {
                var instance = GetVersionInstance();
                if (instance != null)
                {
                    var result = _getGameBuildMethod.Invoke(instance, null);
                    if (result is string version && !string.IsNullOrEmpty(version))
                    {
                        return version;
                    }
                }
            }

            // Fallback: try to get from Application.version (Unity)
            if (_unityEngine != null)
            {
                var appType = _unityEngine.GetType("UnityEngine.Application");
                if (appType != null)
                {
                    var versionProperty = appType.GetProperty("version",
                        BindingFlags.Public | BindingFlags.Static);
                    if (versionProperty != null)
                    {
                        var value = versionProperty.GetValue(null);
                        if (value is string version)
                        {
                            return $"Unity-{version}";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error getting game build: {ex}");
        }

        return "unknown";
    }

    private static object? GetGameStateInstance()
    {
        if (_gameStateType == null) return null;

        var instanceProperty = _gameStateType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceProperty != null)
        {
            return instanceProperty.GetValue(null);
        }

        return null;
    }

    private static object? GetVersionInstance()
    {
        if (_versionType == null) return null;

        var instanceProperty = _versionType.GetProperty("Instance",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (instanceProperty != null)
        {
            return instanceProperty.GetValue(null);
        }

        return null;
    }

    /// <summary>
    /// Gets the current number of accumulated gems in the Gem Mine.
    /// </summary>
    public static int GetAccumulatedGems()
    {
        try
        {
            if (_gemMineGemsProperty != null && _gemMineType != null)
            {
                // Try to get the Gem Mine instance
                var instance = GetGemMineInstance();
                if (instance != null)
                {
                    var value = _gemMineGemsProperty.GetValue(instance);
                    if (value is int gems)
                    {
                        return gems;
                    }
                }
            }

            // Fallback: try to get from PlayerData
            if (_playerDataType != null && _getPlayerDataMethod != null)
            {
                var playerData = _getPlayerDataMethod.Invoke(null, null);
                if (playerData != null)
                {
                    var gemMineProperty = _playerDataType.GetProperty("GemMine",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (gemMineProperty != null)
                    {
                        var gemMine = gemMineProperty.GetValue(playerData);
                        if (gemMine != null)
                        {
                            var gemsProperty = gemMine.GetType().GetProperty("Gems",
                                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            if (gemsProperty != null)
                            {
                                var value = gemsProperty.GetValue(gemMine);
                                if (value is int gems)
                                {
                                    return gems;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error getting accumulated gems: {ex}");
        }

        return 0;
    }

    /// <summary>
    /// Collects the accumulated gems from the Gem Mine.
    /// </summary>
    /// <returns>The number of gems collected, or -1 if collection failed.</returns>
    public static int CollectGemMine()
    {
        try
        {
            var gemsBefore = GetAccumulatedGems();
            
            if (gemsBefore == 0)
            {
                _logger?.LogInfo("Gem Mine has no gems to collect");
                return 0; // Nothing to collect
            }

            if (_collectGemMineMethod != null)
            {
                var instance = GetGemMineInstance();
                if (instance != null)
                {
                    _collectGemMineMethod.Invoke(instance, null);
                    
                    // Give the game a moment to process
                    System.Threading.Thread.Sleep(100);
                    
                    var gemsAfter = GetAccumulatedGems();
                    var collected = gemsBefore - gemsAfter;
                    
                    if (collected > 0)
                    {
                        _logger?.LogInfo($"Collected {collected} gems from Gem Mine");
                        return collected;
                    }
                    else
                    {
                        _logger?.LogWarning("Collect method called but no gems were collected");
                        return gemsBefore; // Assume all were collected
                    }
                }
            }

            _logger?.LogError("Could not find Gem Mine collect method");
            return -1;
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error collecting Gem Mine: {ex}");
            return -1;
        }
    }

    private static object? GetGemMineInstance()
    {
        // Try to get from UI Manager
        if (_uiManagerType != null && _getGemMineControllerMethod != null)
        {
            var uiManagerInstance = _uiManagerType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (uiManagerInstance != null)
            {
                return _getGemMineControllerMethod.Invoke(uiManagerInstance, null);
            }
        }

        // Try to get from GemMineController static Instance
        if (_gemMineType != null)
        {
            var instanceProperty = _gemMineType.GetProperty("Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (instanceProperty != null)
            {
                return instanceProperty.GetValue(null);
            }
        }

        return null;
    }

    public static bool IsInitialized => _initialized;
}