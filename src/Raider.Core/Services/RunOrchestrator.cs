namespace Raider.Core.Services;

using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Orchestrates the execution of a Run for an Account on a specific Instance.
/// </summary>
public sealed class RunOrchestrator
{
    private readonly IGamePort _gamePort;
    private readonly ISnapshotService _snapshotService;
    private readonly INotificationService _notificationService;
    private readonly GameBuild _targetedGameBuild;
    private readonly ILogger<RunOrchestrator> _logger;

    /// <summary>
    /// Gets the targeted game build for this orchestrator.
    /// </summary>
    public GameBuild TargetedGameBuild => _targetedGameBuild;

    public RunOrchestrator(
        IGamePort gamePort,
        ISnapshotService snapshotService,
        INotificationService notificationService,
        GameBuild targetedGameBuild,
        ILogger<RunOrchestrator> logger)
    {
        _gamePort = gamePort;
        _snapshotService = snapshotService;
        _notificationService = notificationService;
        _targetedGameBuild = targetedGameBuild;
        _logger = logger;
    }

    /// <summary>
    /// Executes a complete Run for the given account on the specified instance.
    /// </summary>
    public async Task<Run> ExecuteRunAsync(
        Account account,
        string instanceToken,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var startedAt = DateTimeOffset.UtcNow;
        
        _logger.LogInformation("Starting run {RunId} for account {AccountId} on instance {InstanceToken}", runId, account.Id, instanceToken);

        // Get actual game build
        var actualGameBuild = await _gamePort.GetGameBuildAsync(cancellationToken);
        var isBuildMismatch = _targetedGameBuild != GameBuild.Unknown && !_targetedGameBuild.Matches(actualGameBuild);
        
        if (isBuildMismatch)
        {
            _logger.LogWarning(
                "Game build mismatch: targeted {Targeted}, actual {Actual}",
                _targetedGameBuild.Version,
                actualGameBuild.Version);
        }

        // Account switch, only when the game is on another (or an unknown) Account
        var currentPlayerId = await _gamePort.GetCurrentPlayerIdAsync(cancellationToken);
        if (!string.Equals(currentPlayerId, account.Id, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Switching from {CurrentPlayerId} to account {AccountId}", currentPlayerId ?? "(unknown)", account.Id);
            var switchResult = await _gamePort.SwitchAccountAsync(instanceToken, account, cancellationToken);

            if (switchResult.Outcome != AccountSwitchOutcome.Switched)
            {
                return await StopRunAsync(runId, account, actualGameBuild, switchResult, startedAt, logDirectory, cancellationToken);
            }
        }

        var results = new List<ChoreResult>();

        foreach (var chore in account.Routine.Chores)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var choreStartedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Executing chore {ChoreId} ({ChoreName})", chore.Id, chore.Name);

            var executionResult = await _gamePort.ExecuteChoreAsync(instanceToken, chore, cancellationToken);
            var choreCompletedAt = DateTimeOffset.UtcNow;

            ChoreResult choreResult;

            if (executionResult.Success)
            {
                choreResult = ChoreResult.Success(chore, choreStartedAt, choreCompletedAt, executionResult.GemsCollected, executionResult.Data);
                _logger.LogInformation("Chore {ChoreId} succeeded with {GemsCollected} gems collected", chore.Id, executionResult.GemsCollected);
            }
            else
            {
                // Determine outcome based on build mismatch
                ChoreOutcome outcome = isBuildMismatch 
                    ? ChoreOutcome.BrokenByGameUpdate 
                    : ChoreOutcome.Failed;

                // Get game state and log excerpt for snapshot
                var gameStateJson = await _gamePort.GetGameStateJsonAsync(instanceToken, cancellationToken);
                var pluginLogExcerpt = await _gamePort.GetPluginLogExcerptAsync(instanceToken, 50, cancellationToken);

                // Create chore result with correct outcome for snapshot
                var choreResultForSnapshot = new ChoreResult(
                    chore,
                    outcome,
                    executionResult.ErrorMessage,
                    null,
                    null,
                    choreStartedAt,
                    choreCompletedAt,
                    0);

                // Save failure snapshot
                var snapshotPath = await _snapshotService.SaveFailureSnapshotAsync(
                    logDirectory,
                    choreResultForSnapshot,
                    gameStateJson,
                    pluginLogExcerpt,
                    cancellationToken);

                choreResult = choreResultForSnapshot with { SnapshotPath = snapshotPath };

                _logger.LogError(
                    "Chore {ChoreId} failed with outcome {Outcome}: {Error}",
                    chore.Id,
                    outcome,
                    executionResult.ErrorMessage);
            }

            results.Add(choreResult);
        }

        var completedAt = DateTimeOffset.UtcNow;
        var run = new Run(
            runId,
            account,
            _targetedGameBuild,
            actualGameBuild,
            results,
            startedAt,
            completedAt,
            logDirectory);

        _logger.LogInformation(
            "Run {RunId} completed: {SuccessCount} succeeded, {FailureCount} failed, {BrokenCount} broken by game update",
            runId,
            run.SuccessCount,
            run.FailureCount - run.BrokenByGameUpdateCount,
            run.BrokenByGameUpdateCount);

        // Show notification only if there were failures
        if (run.HasFailures)
        {
            await _notificationService.ShowRunCompletionNotificationAsync(run, cancellationToken);
        }

        return run;
    }

    /// <summary>
    /// Executes a Run that ends with InstanceLost status (Game day not marked done).
    /// </summary>
    public async Task<Run> ExecuteRunWithInstanceLostAsync(
        Account account,
        string instanceToken,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var startedAt = DateTimeOffset.UtcNow;
        
        // We don't execute chores, just return InstanceLost
        var run = new Run(
            runId,
            account,
            _targetedGameBuild,
            GameBuild.Unknown,
            Array.Empty<ChoreResult>(),
            startedAt,
            DateTimeOffset.UtcNow,
            logDirectory,
            RunStatus.InstanceLost,
            "Instance lost mid-run");

        _logger.LogWarning("Run {RunId} ended with InstanceLost for account {AccountId}", runId, account.Id);
        return run;
    }

    private async Task<Run> StopRunAsync(
        string runId,
        Account account,
        GameBuild actualGameBuild,
        AccountSwitchResult switchResult,
        DateTimeOffset startedAt,
        string logDirectory,
        CancellationToken cancellationToken)
    {
        var status = switchResult.Outcome == AccountSwitchOutcome.PasswordRequired
            ? RunStatus.NeedsManualLogin
            : RunStatus.SwitchFailed;

        var run = new Run(
            runId,
            account,
            _targetedGameBuild,
            actualGameBuild,
            Array.Empty<ChoreResult>(),
            startedAt,
            DateTimeOffset.UtcNow,
            logDirectory,
            status,
            switchResult.ErrorMessage);

        if (status == RunStatus.NeedsManualLogin)
        {
            _logger.LogWarning("Run {RunId} stopped: account {AccountId} needs the player to log in by hand", runId, account.Id);
            await _notificationService.ShowManualLoginRequiredNotificationAsync(account, cancellationToken);
        }
        else
        {
            _logger.LogError("Run {RunId} stopped: switching to account {AccountId} failed: {Error}", runId, account.Id, switchResult.ErrorMessage);
            await _notificationService.ShowRunCompletionNotificationAsync(run, cancellationToken);
        }

        return run;
    }
}