namespace Raider.Desktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Raider.Core.Models;
using Raider.Core.Ports;
using Raider.Core.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System;

/// <summary>
/// View model for the Instances and Accounts window.
/// </summary>
public sealed partial class InstancesAndAccountsViewModel : ObservableObject
{
    private readonly IInstanceDetectionService _instanceDetectionService;
    private readonly IAccountStore _accountStore;
    private readonly RunOrchestrator _runOrchestrator;
    private readonly IGameDetectionService _gameDetectionService;
    private readonly PluginConnection _pluginConnection;
    private readonly ILogger<InstancesAndAccountsViewModel> _logger;
    private CancellationTokenSource? _refreshCts;
    private readonly object _instancesLock = new();

    public InstancesAndAccountsViewModel(
        IInstanceDetectionService instanceDetectionService,
        IAccountStore accountStore,
        RunOrchestrator runOrchestrator,
        IGameDetectionService gameDetectionService,
        PluginConnection pluginConnection,
        ILogger<InstancesAndAccountsViewModel> logger)
    {
        _gameDetectionService = gameDetectionService;
        _pluginConnection = pluginConnection;
        _instanceDetectionService = instanceDetectionService;
        _accountStore = accountStore;
        _runOrchestrator = runOrchestrator;
        _logger = logger;

        Instances = new ObservableCollection<InstanceViewModel>();
        Accounts = new ObservableCollection<AccountItemViewModel>();
        
        StatusMessage = "Initializing...";
        LogEntries = new ObservableCollection<string>();
    }

    public ObservableCollection<InstanceViewModel> Instances { get; }

    public ObservableCollection<AccountItemViewModel> Accounts { get; }

    [ObservableProperty]
    private string _statusMessage;

    [ObservableProperty]
    private bool _isRefreshing;

    public ObservableCollection<string> LogEntries { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAllInstancesCommand), nameof(RunAccountCommand))]
    private bool _isRunningAny;

    /// <summary>
    /// Initializes the view model - loads accounts and starts instance detection.
    /// </summary>
    public async Task InitializeAsync()
    {
        await LoadPluginTokenAsync();
        await LoadAccountsAsync();
        await RefreshInstancesAsync();
        StartPeriodicRefresh();
    }

    /// <summary>
    /// Reads the per-install token from the game folder so requests to the plugin are authorized.
    /// Never creates one: a new token only takes effect once the plugin is (re)installed.
    /// </summary>
    private async Task LoadPluginTokenAsync()
    {
        try
        {
            var game = await _gameDetectionService.FindGameInstallationAsync();
            var tokenPath = game == null ? null : Path.Combine(game.GamePath, "raider.token");
            if (tokenPath == null || !File.Exists(tokenPath))
            {
                AddLog("No plugin token found; install the plugin first");
                return;
            }

            _pluginConnection.Token = (await File.ReadAllTextAsync(tokenPath)).Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading plugin token");
            AddLog($"Could not read the plugin token: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads accounts from the store.
    /// </summary>
    private async Task LoadAccountsAsync()
    {
        try
        {
            var accounts = await _accountStore.LoadAsync();
            Accounts.Clear();
            foreach (var account in accounts)
            {
                Accounts.Add(new AccountItemViewModel(account, SaveAccountsInBackground));
            }
            AddLog($"Loaded {accounts.Count} account(s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading accounts");
            AddLog($"Could not load accounts: {ex.Message}");
        }
    }

    /// <summary>
    /// Refreshes the list of detected instances.
    /// </summary>
    [RelayCommand]
    private async Task RefreshInstancesAsync()
    {
        if (IsRefreshing)
            return;

        IsRefreshing = true;
        StatusMessage = "Detecting instances...";
        AddLog("Detecting game instances...");

        try
        {
            var accounts = await _accountStore.LoadAsync();
            var detectedInstances = await _instanceDetectionService.DetectInstancesAsync(accounts);

            lock (_instancesLock)
            {
                // Update existing instances or add new ones
                var detectedIds = new HashSet<string>(detectedInstances.Select(i => i.Id));
                
                // Remove instances that are no longer detected
                for (int i = Instances.Count - 1; i >= 0; i--)
                {
                    if (!detectedIds.Contains(Instances[i].Id))
                    {
                        Instances.RemoveAt(i);
                    }
                }

                // Update or add instances
                foreach (var instance in detectedInstances)
                {
                    var existing = Instances.FirstOrDefault(i => i.Id == instance.Id);
                    if (existing != null)
                    {
                        existing.RefreshState(instance);
                    }
                    else
                    {
                        var vm = new InstanceViewModel(
                            instance,
                            _instanceDetectionService,
                            _accountStore,
                            _runOrchestrator,
                            OnInstanceRunCompleted);
                        Instances.Add(vm);
                    }
                }

                // Re-mark duplicates
                MarkDuplicateInstances();

            }

            // Refresh account names outside the lock (it awaits)
            foreach (var instanceVm in Instances.ToList())
            {
                await instanceVm.RefreshAccountNameAsync();
            }

            StatusMessage = $"Found {Instances.Count} instance(s)";
            AddLog($"Detected {Instances.Count} game instance(s)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting instances");
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error detecting instances: {ex.Message}");
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void MarkDuplicateInstances()
    {
        var accountCounts = Instances
            .Where(i => i.AccountId != null)
            .GroupBy(i => i.AccountId!)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var instance in Instances)
        {
            if (instance.AccountId != null && accountCounts.GetValueOrDefault(instance.AccountId, 0) > 1)
            {
                if (instance.State != InstanceState.Duplicate)
                {
                    instance.State = InstanceState.Duplicate;
                }
            }
            else if (instance.State == InstanceState.Duplicate)
            {
                instance.State = InstanceState.Idle;
            }
        }
    }

    /// <summary>
    /// Runs all idle instances.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunAll))]
    private async Task RunAllInstancesAsync()
    {
        var idleInstances = Instances.Where(i => i.CanRunNow).ToList();
        if (idleInstances.Count == 0)
            return;

        IsRunningAny = true;
        StatusMessage = $"Running {idleInstances.Count} instance(s)...";
        AddLog($"Starting runs for {idleInstances.Count} instance(s)");

        try
        {
            // Run all instances in parallel (each in its own game process)
            var tasks = idleInstances.Select(i => i.RunNowAsync()).ToList();
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running instances");
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsRunningAny = false;
            StatusMessage = "Ready";
        }
    }

    private bool CanRunAll() => !IsRunningAny && Instances.Any(i => i.CanRunNow);

    private void OnInstanceRunCompleted()
    {
        // Update IsRunningAny based on whether any instance is still running
        IsRunningAny = Instances.Any(i => i.State == InstanceState.Running || i.State == InstanceState.Busy);
        
        // Re-check duplicates after runs complete
        MarkDuplicateInstances();
    }

    /// <summary>
    /// Starts periodic instance refresh.
    /// </summary>
    private void StartPeriodicRefresh()
    {
        _refreshCts?.Cancel();
        _refreshCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            while (!_refreshCts.Token.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _refreshCts.Token);
                if (!_refreshCts.Token.IsCancellationRequested)
                {
                    await Application.Current?.Dispatcher.InvokeAsync(() => RefreshInstancesAsync())!;
                }
            }
        }, _refreshCts.Token);
    }

    private bool CanRunAccount(AccountItemViewModel? item) => !IsRunningAny;

    /// <summary>
    /// Runs one Account's ticked Chores in the most recently started game, whichever Account is logged in there.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRunAccount))]
    private async Task RunAccountAsync(AccountItemViewModel? item)
    {
        if (item == null)
            return;

        var account = item.ToAccount();
        if (account.Routine.Chores.Count == 0)
        {
            item.Status = "No Chores ticked";
            return;
        }

        IsRunningAny = true;
        item.Status = "Running...";
        StatusMessage = $"Running {account.Routine.Chores.Count} Chore(s) for {item.DisplayName}...";
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Raider", "logs", DateTime.Now.ToString("yyyyMMdd-HHmmss"), item.Id);

        try
        {
            var run = await _runOrchestrator.ExecuteRunAsync(account, logDirectory);
            item.Status = string.Join(Environment.NewLine, run.Results.Select(DescribeResult));
            StatusMessage = $"{item.DisplayName}: {run.SuccessCount} ok, {run.FailureCount} failed";
            AddLog(StatusMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run failed");
            item.Status = $"Run failed: {ex.Message}";
            StatusMessage = item.Status;
        }
        finally
        {
            IsRunningAny = false;
        }
    }

    /// <summary>One line per Chore: its outcome, plus the plugin's reason when it had nothing to do.</summary>
    private static string DescribeResult(ChoreResult result)
    {
        if (result.IsFailure)
            return $"✗ {result.Chore.Name}: {result.ErrorMessage}";

        var outcome = ReadString(result.Data, "outcome");
        var reason = ReadString(result.Data, "reason");
        return outcome == "NothingToDo"
            ? $"– {result.Chore.Name}: nothing to do{(reason != null ? $" ({reason})" : "")}"
            : $"✓ {result.Chore.Name}: done";
    }

    private static string? ReadString(object? data, string name) =>
        data is System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Object } json
        && json.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;

    [RelayCommand]
    private async Task RemoveAccountAsync(AccountItemViewModel? item)
    {
        if (item == null)
            return;

        Accounts.Remove(item);
        await SaveAccountsAsync();
        AddLog($"Removed account {item.DisplayName}");
        
        // Refresh instances to update duplicate states
        await RefreshInstancesAsync();
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
        
        Application.Current?.Dispatcher.Invoke(() =>
        {
            LogEntries.Insert(0, entry);
            while (LogEntries.Count > 100)
            {
                LogEntries.RemoveAt(LogEntries.Count - 1);
            }
        });
    }
}