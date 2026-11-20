namespace Raider.Desktop.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Raider.Core.Models;
using Raider.Core.Services;
using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System;

/// <summary>
/// View model for a detected game Instance.
/// </summary>
public sealed partial class InstanceViewModel : ObservableObject
{
    private readonly IInstanceDetectionService _instanceDetectionService;
    private readonly IAccountStore _accountStore;
    private readonly RunOrchestrator _runOrchestrator;
    private readonly Action _onInstanceRunCompleted;
    private CancellationTokenSource? _runCts;

    public InstanceViewModel(
        Instance instance,
        IInstanceDetectionService instanceDetectionService,
        IAccountStore accountStore,
        RunOrchestrator runOrchestrator,
        Action onInstanceRunCompleted)
    {
        _instanceDetectionService = instanceDetectionService;
        _accountStore = accountStore;
        _runOrchestrator = runOrchestrator;
        _onInstanceRunCompleted = onInstanceRunCompleted;

        Id = instance.Id;
        ProcessId = instance.ProcessId;
        GamePath = instance.GamePath;
        AccountId = instance.AccountId;
        State = instance.State;
        LastRunStartedAt = instance.LastRunStartedAt;
        LastRunCompletedAt = instance.LastRunCompletedAt;
        LastRunStatus = instance.LastRunStatus;
        LastRunMessage = instance.LastRunMessage;
    }

    public string Id { get; }

    public int ProcessId { get; }

    public string GamePath { get; }

    public string? AccountId { get; }

    [ObservableProperty]
    private InstanceState _state;

    [ObservableProperty]
    private DateTimeOffset? _lastRunStartedAt;

    [ObservableProperty]
    private DateTimeOffset? _lastRunCompletedAt;

    [ObservableProperty]
    private RunStatus? _lastRunStatus;

    [ObservableProperty]
    private string? _lastRunMessage;

    [ObservableProperty]
    private string _accountName = "Unknown Account";

    public async Task RefreshAccountNameAsync()
    {
        if (AccountId == null)
        {
            AccountName = "No Account";
            return;
        }
        try
        {
            var accounts = await _accountStore.LoadAsync();
            var account = accounts.FirstOrDefault(a => a.Id == AccountId);
            AccountName = account?.DisplayName ?? AccountId;
        }
        catch
        {
            AccountName = AccountId;
        }
    }

    public string StateDisplay => State switch
    {
        InstanceState.Idle => "Idle",
        InstanceState.Busy => "Busy",
        InstanceState.Running => "Running",
        InstanceState.DoneToday => "Done Today",
        InstanceState.Failed => "Failed",
        InstanceState.Duplicate => "Duplicate",
        _ => State.ToString()
    };

    public bool CanRunNow => State is InstanceState.Idle or InstanceState.Failed or InstanceState.DoneToday;

    [RelayCommand(CanExecute = nameof(CanRunNow))]
    private async Task RunNowAsync()
    {
        if (AccountId == null)
        {
            LastRunMessage = "No account associated with this instance";
            State = InstanceState.Failed;
            return;
        }

        _runCts?.Cancel();
        _runCts = new CancellationTokenSource();

        State = InstanceState.Running;
        LastRunStartedAt = DateTimeOffset.UtcNow;
        LastRunMessage = "Running...";

        try
        {
            var accounts = await _accountStore.LoadAsync();
            var account = accounts.FirstOrDefault(a => a.Id == AccountId);
            if (account == null)
            {
                LastRunMessage = "Account not found";
                State = InstanceState.Failed;
                return;
            }

            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Raider", "logs", "instances", Id, DateTime.Now.ToString("yyyyMMdd-HHmmss"));

            var run = await _runOrchestrator.ExecuteRunAsync(account, logDirectory, _runCts.Token);
            
            LastRunCompletedAt = DateTimeOffset.UtcNow;
            LastRunStatus = run.Status;
            
            if (run.Status == RunStatus.Completed)
            {
                State = run.HasFailures ? InstanceState.Failed : InstanceState.DoneToday;
                LastRunMessage = $"Done: {run.SuccessCount} ok, {run.FailureCount} failed";
            }
            else if (run.Status == RunStatus.NeedsManualLogin)
            {
                State = InstanceState.Failed;
                LastRunMessage = "Needs manual login in Plarium Play";
            }
            else
            {
                State = InstanceState.Failed;
                LastRunMessage = run.StatusMessage ?? "Account switch failed";
            }
        }
        catch (OperationCanceledException)
        {
            State = InstanceState.Idle;
            LastRunMessage = "Cancelled";
        }
        catch (Exception ex)
        {
            State = InstanceState.Failed;
            LastRunMessage = $"Error: {ex.Message}";
        }
        finally
        {
            _onInstanceRunCompleted();
        }
    }

    public void RefreshState(Instance instance)
    {
        State = instance.State;
        LastRunStartedAt = instance.LastRunStartedAt;
        LastRunCompletedAt = instance.LastRunCompletedAt;
        LastRunStatus = instance.LastRunStatus;
        LastRunMessage = instance.LastRunMessage;
    }
}