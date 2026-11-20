namespace Raider.Desktop.ViewModels;

namespace Raider.Desktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Core.Ports;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// View model for the main window showing plugin status and game info.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IGameDetectionService _gameDetectionService;
    private readonly IPluginInstallerService _pluginInstallerService;
    private readonly IPluginHealthClient _pluginHealthClient;
    private readonly IGamePort _gamePort;
    private readonly ILogger<MainWindowViewModel> _logger;
    
    private GameInstallation? _gameInstallation;
    private string _token = string.Empty;
    private CancellationTokenSource? _healthCheckCts;
    private readonly object _lock = new();

    public MainWindowViewModel(
        IGameDetectionService gameDetectionService,
        IPluginInstallerService pluginInstallerService,
        IPluginHealthClient pluginHealthClient,
        IGamePort gamePort,
        ILogger<MainWindowViewModel> logger)
    {
        _gameDetectionService = gameDetectionService;
        _pluginInstallerService = pluginInstallerService;
        _pluginHealthClient = pluginHealthClient;
        _gamePort = gamePort;
        _logger = logger;

        PluginStatus = PluginInstallState.NotInstalled;
        GameBuild = "Unknown";
        IsInGame = false;
        PluginVersion = "Unknown";
        StatusMessage = "Initializing...";
        LogEntries = new ObservableCollection<string>();
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
    }

    [ObservableProperty]
    private PluginInstallState _pluginStatus;

    [ObservableProperty]
    private string _gameBuild;

    [ObservableProperty]
    private bool _isInGame;

    [ObservableProperty]
    private string _pluginVersion;

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isInstalling;

    public ObservableCollection<string> LogEntries { get; }

    /// <summary>
    /// Initializes the view model - detects game and checks plugin status.
    /// </summary>
    public async Task InitializeAsync()
    {
        await DetectGameAndCheckStatusAsync();
        StartHealthMonitoring();
    }

    /// <summary>
    /// Detects the game installation and checks plugin status.
    /// </summary>
    [RelayCommand]
    private async Task DetectGameAndCheckStatusAsync()
    {
        IsBusy = true;
        StatusMessage = "Detecting game installation...";
        AddLog("Detecting game installation...");

        try
        {
            _gameInstallation = await _gameDetectionService.FindGameInstallationAsync();
            
            if (_gameInstallation == null)
            {
                StatusMessage = "Raid: Shadow Legends not found. Install via Plarium Play.";
                AddLog("Game not found. Please install Raid via Plarium Play.");
                PluginStatus = PluginInstallState.NotInstalled;
                return;
            }

            AddLog($"Game found at: {_gameInstallation.GamePath} (Plarium Play: {_gameInstallation.IsPlariumPlay})");
            
            // Get or create token
            _token = await _pluginInstallerService.GetOrCreateTokenAsync(_gameInstallation);
            AddLog("Token retrieved/generated");

            // Check install state
            var state = await _pluginInstallerService.GetInstallStateAsync(_gameInstallation);
            PluginStatus = state;
            
            if (state == PluginInstallState.NotInstalled)
            {
                StatusMessage = "Plugin not installed. Click Install to set up.";
                AddLog("Plugin not installed");
            }
            else
            {
                StatusMessage = "Plugin installed. Checking connection...";
                AddLog("Plugin installed, checking health...");
                await CheckHealthAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting game");
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Installs the plugin and BepInEx.
    /// </summary>
    [RelayCommand]
    private async Task InstallPluginAsync()
    {
        if (_gameInstallation == null)
        {
            StatusMessage = "No game installation found";
            return;
        }

        IsInstalling = true;
        StatusMessage = "Installing plugin...";
        AddLog("Starting plugin installation...");

        try
        {
            var progress = new Progress<string>(msg => 
            {
                StatusMessage = msg;
                AddLog(msg);
            });

            var result = await _pluginInstallerService.InstallAsync(_gameInstallation, _token, progress);
            
            if (result.Success)
            {
                PluginStatus = PluginInstallState.Installed;
                StatusMessage = "Plugin installed successfully. Start the game to connect.";
                AddLog($"Plugin v{result.InstalledPluginVersion} and BepInEx v{result.InstalledBepInExVersion} installed");
                
                // Start health monitoring after install
                StartHealthMonitoring();
            }
            else
            {
                StatusMessage = $"Installation failed: {result.ErrorMessage}";
                AddLog($"Installation failed: {result.ErrorMessage}");
                PluginStatus = PluginInstallState.NotInstalled;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error installing plugin");
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
            PluginStatus = PluginInstallState.NotInstalled;
        }
        finally
        {
            IsInstalling = false;
        }
    }

    /// <summary>
    /// Checks the plugin health endpoint.
    /// </summary>
    [RelayCommand]
    private async Task CheckHealthAsync()
    {
        if (_gameInstallation == null || string.IsNullOrEmpty(_token))
            return;

        try
        {
            var health = await _pluginHealthClient.GetHealthAsync(_token);
            
            if (health != null)
            {
                PluginStatus = PluginInstallState.Connected;
                GameBuild = health.GameBuild.Version;
                IsInGame = health.IsInGame;
                PluginVersion = health.PluginVersion;
                StatusMessage = health.IsInGame ? "Connected - In Game" : "Connected - In Menu";
                AddLog($"Health check OK: InGame={health.IsInGame}, GameBuild={health.GameBuild.Version}, Plugin={health.PluginVersion}");
            }
            else
            {
                // Plugin installed but not responding
                if (PluginStatus == PluginInstallState.Connected)
                {
                    PluginStatus = PluginInstallState.Installed;
                }
                StatusMessage = "Plugin installed but not responding (game not running?)";
                AddLog("Health check failed - plugin not responding");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking health");
            AddLog($"Health check error: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts periodic health monitoring.
    /// </summary>
    private void StartHealthMonitoring()
    {
        _healthCheckCts?.Cancel();
        _healthCheckCts = new CancellationTokenSource();
        
        _ = Task.Run(async () =>
        {
            while (!_healthCheckCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), _healthCheckCts.Token);
                if (!_healthCheckCts.Token.IsCancellationRequested)
                {
                    await CheckHealthAsync();
                }
            }
        }, _healthCheckCts.Token);
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var entry = $"[{timestamp}] {message}";
        
        // Run on UI thread
        Application.Current?.Dispatcher.Invoke(() =>
        {
            LogEntries.Insert(0, entry);
            // Keep only last 100 entries
            while (LogEntries.Count > 100)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });
    }
}