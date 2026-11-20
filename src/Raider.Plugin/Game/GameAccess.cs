using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Reflection;

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

    // Cached types
    private static Type? _gemMineType;
    private static Type? _playerDataType;
    private static Type? _uiManagerType;

    // Cached methods
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

        // Try to find Gem Mine related types
        // These are common naming patterns in Raid: Shadow Legends
        _gemMineType = FindType("GemMineController", "GemMineView", "GemMineModel", "GemMineData");
        _playerDataType = FindType("PlayerData", "PlayerModel", "AccountData");
        _uiManagerType = FindType("UIManager", "WindowManager", "ViewManager");

        if (_gemMineType != null)
        {
            // Try to find the collect method
            _collectGemMineMethod = _gemMineType.GetMethod("Collect", 
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetMethod("CollectGems",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                ?? _gemMineType.GetMethod("OnCollectClick",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            // Try to find the gems property
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

        _logger?.LogDebug($"Cached types: GemMine={_gemMineType?.Name}, PlayerData={_playerDataType?.Name}, UIManager={_uiManagerType?.Name}");
        _logger?.LogDebug($"Cached methods: Collect={_collectGemMineMethod?.Name}, GemsProperty={_gemMineGemsProperty?.Name}");
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