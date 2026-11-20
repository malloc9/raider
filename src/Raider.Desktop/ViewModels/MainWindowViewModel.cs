namespace Raider.Desktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Core.Ports;
using System.Collections.ObjectModel;
using System.IO;
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
    private readonly IAccountStore _accountStore;
    private readonly RunCoordinator _runCoordinator;
    private readonly IInstanceRegistry _instanceRegistry;
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
        IAccountStore accountStore,
        RunCoordinator runCoordinator,
        IInstanceRegistry instanceRegistry,
        ILogger<MainWindowViewModel> logger)
    {
        _gameDetectionService = gameDetectionService;
        _pluginInstallerService = pluginInstallerService;
        _pluginHealthClient = pluginHealthClient;
        _gamePort = gamePort;
        _accountStore = accountStore;
        _runCoordinator = runCoordinator;
        _instanceRegistry = instanceRegistry;
        _logger = logger;
        Accounts = new ObservableCollection<AccountItemViewModel>();
        _newDisplayName = string.Empty;

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
        await LoadAccountsAsync();
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

    // ---- Accounts ----

    /// <summary>The player's Accounts, each with its own Routine.</summary>
    public ObservableCollection<AccountItemViewModel> Accounts { get; }

    /// <summary>Display name of the Account to add.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAccountCommand))]
    private string _newDisplayName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAccountCommand), nameof(RunAllAccountsCommand))]
    private bool _isRunning;

    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accountStore.LoadAsync();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                var vm = new AccountItemViewModel(account, SaveAccountsInBackground);
                vm.Initialize(account);
                Accounts.Add(vm);
            }
            AddLog($"Loaded {accounts.Count} account(s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accounts");
            AddLog($"Could not load accounts: {ex.Message}");
        }
    }

    private bool CanAddAccount() => !string.IsNullOrWhiteSpace(NewDisplayName);

    [RelayCommand(CanExecute = nameof(CanAddAccount))]
    private async Task AddAccountAsync()
    {
        var displayName = NewDisplayName.Trim();
        if (Accounts.Any(a => string.Equals(a.DisplayName, displayName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = $"Account {displayName} is already added";
            return;
        }

        var account = new Account(Guid.NewGuid().ToString("N")[..12], displayName, Routine.Default());
        Accounts.Add(new AccountItemViewModel(account, SaveAccountsInBackground));
        NewDisplayName = string.Empty;

        await SaveAccountsAsync();
        AddLog($"Added account {displayName}");
    }

    [RelayCommand]
    private async Task RemoveAccountAsync(AccountItemViewModel? item)
    {
        if (item == null || IsRunning)
            return;

        Accounts.Remove(item);
        await SaveAccountsAsync();
        AddLog($"Removed account {item.DisplayName}");
    }

    private bool CanRun(AccountItemViewModel? item) => !IsRunning;

    [RelayCommand(CanExecute = nameOf(CanRun))]
    private async Task RunAccountAsync(AccountItemViewModel? item)
    {
        if (item == null) return;
        await RunAccountsAsync(new[] { item });
    }

    private bool CanRunAll() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanRunAll))]
    private async Task RunAllAccountsAsync() => await RunAccountsAsync(Accounts.ToList());

    private async Task RunAccountsAsync(IReadOnlyList<AccountItemViewModel> items)
    {
        if (items.Count == 0)
            return;

        IsRunning = true;
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Raider", "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss"));

        try
        {
            // Manual runs through RunCoordinator (bypass game-day gating)
            foreach (var item in items)
            {
                item.Status = "Running...";
                item.NeedsManualLogin = false;
                AddLog($"Run started for {item.DisplayName}");

                var run = await _runCoordinator.RunManualAsync(item.ToAccount().Id);
                if (run != null)
                {
                    ReportRun(item, run);
                }
                else
                {
                    item.Status = "No instance found for this account";
                    AddLog($"{item.DisplayName}: No game instance found");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run failed");
            StatusMessage = $"Run failed: {ex.Message}";
            AddLog($"Run failed: {ex.Message}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void ReportRun(AccountItemViewModel item, Run run)
    {
        switch (run.Status)
        {
            case RunStatus.NeedsManualLogin:
                item.NeedsManualLogin = true;
                item.Status = "Log in by hand in Plarium Play, then run again";
                StatusMessage = $"{item.DisplayName} needs you to log in by hand in Plarium Play";
                AddLog($"{item.DisplayName}: Plarium Play asks for the password. Log in by hand; continuing with the next account.");
                break;
            case RunStatus.SwitchFailed:
                item.Status = $"Account switch failed: {run.StatusMessage}";
                AddLog($"{item.DisplayName}: account switch failed: {run.StatusMessage}");
                break;
            case RunStatus.InstanceLost:
                item.Status = "Instance lost mid-run";
                AddLog($"{item.DisplayName}: instance lost mid-run, game day not marked done");
                break;
            default:
                item.Status = $"Done: {run.SuccessCount} ok, {run.FailureCount} failed";
                AddLog($"{item.DisplayName}: {run.SuccessCount} chore(s) succeeded, {run.FailureCount} failed");
                break;
        }
    }

    private void SaveAccountsInBackground() => _ = SaveAccountsAsync();

    private async Task SaveAccountsAsync()
    {
        try
        {
            await _accountStore.SaveAsync(Accounts.Select(a => a.ToAccount()).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving accounts");
            AddLog($"Could not save accounts: {ex.Message}");
        }
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