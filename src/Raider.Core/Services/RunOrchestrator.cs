namespace Raider.Core.Services;

using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Orchestrates the execution of a Run for an Account.
/// </summary>
public sealed class RunOrchestrator
{
    private readonly IGamePort _gamePort;
    private readonly ISnapshotService _snapshotService;
    private readonly INotificationService _notificationService;
    private readonly GameBuild _targetedGameBuild;
    private readonly ILogger<RunOrchestrator> _logger;

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
    /// Executes a complete Run for the given account.
    /// </summary>
    public async Task<Run> ExecuteRunAsync(
        Account account,
        string logDirectory,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var startedAt = DateTimeOffset.UtcNow;
        
        _logger.LogInformation("Starting run {RunId} for account {AccountId}", runId, account.Id);

        // Get actual game build
        var actualGameBuild = await _gamePort.GetGameBuildAsync(cancellationToken);
        var isBuildMismatch = !_targetedGameBuild.Matches(actualGameBuild);
        
        if (isBuildMismatch)
        {
            _logger.LogWarning(
                "Game build mismatch: targeted {Targeted}, actual {Actual}",
                _targetedGameBuild.Version,
                actualGameBuild.Version);
        }

        // Switch to account
        await _gamePort.SwitchAccountAsync(account, cancellationToken);

        var results = new List<ChoreResult>();

        foreach (var chore in account.Routine.Chores)
        {
            var choreStartedAt = DateTimeOffset.UtcNow;
            _logger.LogInformation("Executing chore {ChoreId} ({ChoreName})", chore.Id, chore.Name);

            var executionResult = await _gamePort.ExecuteChoreAsync(chore, cancellationToken);
            var choreCompletedAt = DateTimeOffset.UtcNow;

            ChoreResult choreResult;

            if (executionResult.Success)
            {
                choreResult = ChoreResult.Success(chore, choreStartedAt, choreCompletedAt, executionResult.Data);
                _logger.LogInformation("Chore {ChoreId} succeeded", chore.Id);
            }
            else
            {
                // Determine outcome based on build mismatch
                ChoreOutcome outcome = isBuildMismatch 
                    ? ChoreOutcome.BrokenByGameUpdate 
                    : ChoreOutcome.Failed;

                // Get game state and log excerpt for snapshot
                var gameStateJson = await _gamePort.GetGameStateJsonAsync(cancellationToken);
                var pluginLogExcerpt = await _gamePort.GetPluginLogExcerptAsync(50, cancellationToken);

                // Create chore result with correct outcome for snapshot
                var choreResultForSnapshot = new ChoreResult(
                    chore,
                    outcome,
                    executionResult.ErrorMessage,
                    null,
                    null,
                    choreStartedAt,
                    choreCompletedAt);

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
}