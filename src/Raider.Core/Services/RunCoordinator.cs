namespace Raider.Core.Services;

using System.Collections.Concurrent;
using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Coordinates concurrent Runs across multiple Instances.
/// Handles game-day gating, busy deferral, duplicate detection, and InstanceLost.
/// </summary>
public sealed class RunCoordinator : IDisposable
{
    private readonly IInstanceRegistry _instanceRegistry;
    private readonly IAccountStore _accountStore;
    private readonly RunOrchestrator _orchestrator;
    private readonly ILogger<RunCoordinator> _logger;
    private readonly ConcurrentDictionary<string, RunTask> _runningTasks = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _instanceBusySince = new();
    private readonly CancellationTokenSource _shutdownCts = new();
    private readonly SemaphoreSlim _instanceLock = new(1, 1);
    private bool _disposed;

    public RunCoordinator(
        IInstanceRegistry instanceRegistry,
        IAccountStore accountStore,
        RunOrchestrator orchestrator,
        ILogger<RunCoordinator> logger)
    {
        _instanceRegistry = instanceRegistry;
        _accountStore = accountStore;
        _orchestrator = orchestrator;
        _logger = logger;

        _instanceRegistry.Changed += OnInstancesChanged;
    }

    /// <summary>
    /// Starts the coordinator (begins monitoring instances and auto-running).
    /// </summary>
    public void Start()
    {
        _instanceRegistry.Start();
        _logger.LogInformation("RunCoordinator started");
    }

    /// <summary>
    /// Stops the coordinator.
    /// </summary>
    public void Stop()
    {
        _instanceRegistry.Stop();
        _shutdownCts.Cancel();
        _logger.LogInformation("RunCoordinator stopped");
    }

    /// <summary>
    /// Manually triggers a Run for a specific account on its instance.
    /// This bypasses game-day gating (manual Run now / Run all).
    /// </summary>
    public async Task<Run?> RunManualAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var account = accounts.FirstOrDefault(a => a.Id == accountId);
        if (account == null)
        {
            _logger.LogWarning("Manual run requested for unknown account {AccountId}", accountId);
            return null;
        }

        var instance = _instanceRegistry.GetInstanceByPlayerId(account.Id);
        if (instance == null)
        {
            _logger.LogWarning("No instance found for account {AccountId}", accountId);
            return null;
        }

        // Manual runs ignore game-day gating and busy state
        return await ExecuteRunInternalAsync(account, instance, isManual: true, cancellationToken);
    }

    /// <summary>
    /// Manually triggers Runs for all accounts (Run All).
    /// This bypasses game-day gating.
    /// </summary>
    public async Task<IReadOnlyList<Run>> RunAllManualAsync(CancellationToken cancellationToken = default)
    {
        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var runs = new List<Run>();

        foreach (var account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var instance = _instanceRegistry.GetInstanceByPlayerId(account.Id);
            if (instance != null)
            {
                var run = await ExecuteRunInternalAsync(account, instance, isManual: true, cancellationToken);
                if (run != null) runs.Add(run);
            }
        }

        return runs;
    }

    private void OnInstancesChanged(object? sender, InstanceRegistryChangedEventArgs e)
    {
        // Handle new instances - auto-run if eligible
        foreach (var instance in e.Added)
        {
            if (instance.Status == InstanceStatus.Ready && instance.PlayerId != null)
            {
                _ = TryAutoRunAsync(instance, _shutdownCts.Token);
            }
        }

        // Handle instance lost mid-run
        foreach (var instance in e.Updated.Where(i => i.Status == InstanceStatus.InstanceLost))
        {
            HandleInstanceLost(instance);
        }

        // Handle instances becoming ready again after busy/waiting
        foreach (var instance in e.Updated.Where(i => i.Status == InstanceStatus.Ready && i.PlayerId != null))
        {
            _ = TryAutoRunAsync(instance, _shutdownCts.Token);
        }
    }

    private async Task TryAutoRunAsync(Instance instance, CancellationToken cancellationToken)
    {
        if (instance.PlayerId == null) return;

        // Check if already running
        if (_runningTasks.ContainsKey(instance.Id)) return;

        // Load account
        var accounts = await _accountStore.LoadAsync(cancellationToken);
        var account = accounts.FirstOrDefault(a => a.Id == instance.PlayerId);
        
        if (account == null)
        {
            // Auto-register unknown player ID
            account = new Account(
                Id: instance.PlayerId!,
                DisplayName: instance.PlayerName ?? instance.PlayerId!,
                Routine: Routine.Default()
            );
            
            var allAccounts = accounts.ToList();
            allAccounts.Add(account);
            await _accountStore.SaveAsync(allAccounts, cancellationToken);
            
            _logger.LogInformation("Auto-registered account {AccountId} ({DisplayName})", account.Id, account.DisplayName);
        }

        // Game-day gating: skip if already ran today (and not manual)
        if (GameDay.HasRunToday(account))
        {
            _logger.LogDebug("Account {AccountId} already ran today (game day {GameDay}), skipping auto-run", account.Id, account.LastRunGameDay);
            return;
        }

        // Check if instance is busy or waiting
        if (instance.Status == InstanceStatus.Busy || instance.Status == InstanceStatus.Waiting)
        {
            _logger.LogDebug("Instance {InstanceId} is busy/waiting, will retry later", instance.Id);
            return;
        }

        // Check for duplicate (another instance with same player ID already running)
        var duplicateInstance = _instanceRegistry.GetInstances()
            .FirstOrDefault(i => i.PlayerId == instance.PlayerId && i.Id != instance.Id && i.Status == InstanceStatus.Busy);
        
        if (duplicateInstance != null)
        {
            _logger.LogInformation("Instance {InstanceId} is duplicate of {OtherInstanceId}, marking as Duplicate", instance.Id, duplicateInstance.Id);
            // The InstanceRegistry already marks duplicates, but we double-check here
            return;
        }

        await ExecuteRunInternalAsync(account, instance, isManual: false, cancellationToken);
    }

    private async Task<Run?> ExecuteRunInternalAsync(Account account, Instance instance, bool isManual, CancellationToken cancellationToken)
    {
        var runTask = new RunTask
        {
            Account = account,
            Instance = instance,
            IsManual = isManual,
            CancellationToken = cancellationToken
        };

        _runningTasks[instance.Id] = runTask;
        _instanceBusySince[instance.Id] = DateTimeOffset.UtcNow;
        _instanceRegistry.MarkBusy(instance.Id);

        try
        {
            var logDirectory = Path.Combine(
                Path.GetTempPath(), "raider-logs", account.Id, instance.Id, DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));
            
            var run = await _orchestrator.ExecuteRunAsync(account, instance.Token, logDirectory, cancellationToken);

            // Check if instance was lost during run (status changed to InstanceLost)
            var currentInstance = _instanceRegistry.GetInstances().FirstOrDefault(i => i.Id == instance.Id);
            if (currentInstance?.Status == InstanceStatus.InstanceLost)
            {
                // Instance lost mid-run - mark as InstanceLost, Game day NOT marked done
                run = run with { Status = RunStatus.InstanceLost, StatusMessage = "Instance lost mid-run" };
                _logger.LogWarning("Run {RunId} ended with InstanceLost for account {AccountId}", run.Id, account.Id);
                return run;
            }

            // Update account with run result (only for non-InstanceLost runs)
            var outcome = run.Status == RunStatus.InstanceLost ? RunOutcome.InstanceLost : 
                         run.HasFailures ? RunOutcome.Failed : RunOutcome.Completed;
            var updatedAccount = GameDay.WithRunResult(account, outcome);
            var allAccounts = (await _accountStore.LoadAsync(cancellationToken)).ToList();
            var idx = allAccounts.FindIndex(a => a.Id == account.Id);
            if (idx >= 0)
            {
                allAccounts[idx] = updatedAccount;
                await _accountStore.SaveAsync(allAccounts, cancellationToken);
            }

            _logger.LogInformation("Run {RunId} completed for account {AccountId} with outcome {Outcome}", run.Id, account.Id, outcome);
            return run;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Run cancelled for account {AccountId}", account.Id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Run failed for account {AccountId}", account.Id);
            
            var failedRun = new Run(
                Guid.NewGuid().ToString("N")[..12],
                account,
                _orchestrator.TargetedGameBuild,
                GameBuild.Unknown,
                Array.Empty<ChoreResult>(),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                "",
                RunStatus.SwitchFailed,
                ex.Message);

            return failedRun;
        }
        finally
        {
            _runningTasks.TryRemove(instance.Id, out _);
            _instanceBusySince.TryRemove(instance.Id, out _);
            _instanceRegistry.MarkFree(instance.Id);
        }
    }

    private void HandleInstanceLost(Instance instance)
    {
        if (_runningTasks.TryGetValue(instance.Id, out var runTask))
        {
            // The run will be marked as InstanceLost when it completes
            _logger.LogWarning("Instance {InstanceId} lost, run for account {AccountId} will end with InstanceLost", instance.Id, runTask.Account.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _instanceRegistry.Changed -= OnInstancesChanged;
        _shutdownCts.Cancel();
        _shutdownCts.Dispose();
        _instanceLock.Dispose();
    }

    private sealed class RunTask
    {
        public Account Account { get; init; } = null!;
        public Instance Instance { get; init; } = null!;
        public bool IsManual { get; init; }
        public CancellationToken CancellationToken { get; init; }
        public CancellationTokenSource? CancellationTokenSource { get; init; }
    }
}