namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Orchestration tests against a fake Game port covering notification-only-on-failure and build-mismatch marking.
/// </summary>
public sealed class RunOrchestratorTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly Account _account;
    private readonly Chore _chore1;
    private readonly Chore _chore2;
    private readonly string _logDirectory;

    public RunOrchestratorTests()
    {
        _gamePort = new FakeGamePort();
        _snapshotService = new FakeSnapshotService();
        _notificationService = new FakeNotificationService();
        _logDirectory = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
        
        _orchestrator = TestDataBuilder.CreateOrchestrator(
            _gamePort,
            _snapshotService,
            _notificationService);

        _chore1 = TestDataBuilder.CreateChore("chore-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        _chore2 = TestDataBuilder.CreateChore("chore-2", "Collect Gem Mine", ChoreType.CollectGemMine);
        
        _account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(_chore1, _chore2));

        Directory.CreateDirectory(_logDirectory);
    }

    [Fact]
    public async Task Run_WithAllChoresSucceeding_DoesNotRaiseNotification()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(true));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(2);
        run.FailureCount.Should().Be(0);
        
        // No notification should be raised for successful runs
        _notificationService.NotificationCount.Should().Be(0);
        _notificationService.LastNotifiedRun.Should().BeNull();
        
        // No snapshots should be saved
        _snapshotService.SnapshotCount.Should().Be(0);
    }

    [Fact]
    public async Task Run_WithOneChoreFailing_RaisesNotification()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(false, "Failed to find Gem Mine button"));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeTrue();
        run.SuccessCount.Should().Be(1);
        run.FailureCount.Should().Be(1);
        run.BrokenByGameUpdateCount.Should().Be(0);
        
        // Notification should be raised for failed runs
        _notificationService.NotificationCount.Should().Be(1);
        _notificationService.LastNotifiedRun.Should().Be(run);
        
        // Snapshot should be saved for the failed chore
        _snapshotService.SnapshotCount.Should().Be(1);
        _snapshotService.SavedSnapshots.Should().HaveCount(1);
        _snapshotService.SavedSnapshots[0].Result.Chore.Id.Should().Be("chore-2");
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresFailing_RaisesSingleNotification()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(false, "Error 1"));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(false, "Error 2"));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeTrue();
        run.SuccessCount.Should().Be(0);
        run.FailureCount.Should().Be(2);
        
        // Only one notification for the entire run
        _notificationService.NotificationCount.Should().Be(1);
        
        // Snapshots for both failed chores
        _snapshotService.SnapshotCount.Should().Be(2);
    }

    [Fact]
    public async Task Run_WithGameBuildMismatch_MarksFailingChoresAsBrokenByGameUpdate()
    {
        // Arrange
        var targetedBuild = TestDataBuilder.CreateGameBuild("1.0.0");
        var actualBuild = TestDataBuilder.CreateGameBuild("1.1.0");
        
        _gamePort.SetGameBuild(actualBuild);
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(false, "Method not found: CollectGemMine"));

        var orchestrator = TestDataBuilder.CreateOrchestrator(
            _gamePort,
            _snapshotService,
            _notificationService,
            targetedBuild);

        // Act
        var run = await orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.IsGameBuildMismatch.Should().BeTrue();
        run.TargetedGameBuild.Version.Should().Be("1.0.0");
        run.ActualGameBuild.Version.Should().Be("1.1.0");
        
        run.SuccessCount.Should().Be(1);
        run.FailureCount.Should().Be(1);
        run.BrokenByGameUpdateCount.Should().Be(1);
        
        // The failing chore should be marked as BrokenByGameUpdate
        var failedResult = run.Results.First(r => r.Outcome == ChoreOutcome.BrokenByGameUpdate);
        failedResult.Chore.Id.Should().Be("chore-2");
        failedResult.Outcome.Should().Be(ChoreOutcome.BrokenByGameUpdate);
        
        // Snapshot should still be saved
        _snapshotService.SnapshotCount.Should().Be(1);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.BrokenByGameUpdate);
        
        // Notification should be raised
        _notificationService.NotificationCount.Should().Be(1);
    }

    [Fact]
    public async Task Run_WithGameBuildMismatchButAllChoresSucceeding_DoesNotRaiseNotification()
    {
        // Arrange
        var targetedBuild = TestDataBuilder.CreateGameBuild("1.0.0");
        var actualBuild = TestDataBuilder.CreateGameBuild("1.1.0");
        
        _gamePort.SetGameBuild(actualBuild);
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(true));

        var orchestrator = TestDataBuilder.CreateOrchestrator(
            _gamePort,
            _snapshotService,
            _notificationService,
            targetedBuild);

        // Act
        var run = await orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.IsGameBuildMismatch.Should().BeTrue();
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(2);
        run.FailureCount.Should().Be(0);
        run.BrokenByGameUpdateCount.Should().Be(0);
        
        // No notification when all chores succeed, even with build mismatch
        _notificationService.NotificationCount.Should().Be(0);
        
        // No snapshots
        _snapshotService.SnapshotCount.Should().Be(0);
    }

    [Fact]
    public async Task FailedChore_ProducesSnapshotFileNextToLogs()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(false, "Test error"));
        _gamePort.SetGameStateJson("{\"player\":{\"id\":\"123\",\"level\":50}}");
        _gamePort.SetPluginLogExcerpt("[ERROR] Failed to click button\n[DEBUG] Retrying...");

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        _snapshotService.SnapshotCount.Should().Be(1);
        
        var snapshot = _snapshotService.SavedSnapshots[0];
        snapshot.Result.Chore.Id.Should().Be("chore-1");
        snapshot.Result.Outcome.Should().Be(ChoreOutcome.Failed);
        snapshot.Result.ErrorMessage.Should().Be("Test error");
        snapshot.Result.SnapshotPath.Should().NotBeNullOrEmpty();
        snapshot.GameStateJson.Should().Contain("player");
        snapshot.PluginLogExcerpt.Should().Contain("ERROR");
        
        // The snapshot path should be in the log directory
        snapshot.Result.SnapshotPath!.Should().StartWith(_logDirectory);
    }

    [Fact]
    public async Task Run_Summary_ContainsCorrectCounts()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(false, "Error"));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.SuccessCount.Should().Be(1);
        run.FailureCount.Should().Be(1);
        run.BrokenByGameUpdateCount.Should().Be(0);
        run.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task CollectGemMine_WithGemsCollected_ReportsGemsInResult()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(true, null, 150));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.SuccessCount.Should().Be(2);
        var gemMineResult = run.Results.First(r => r.Chore.Type == ChoreType.CollectGemMine);
        gemMineResult.Outcome.Should().Be(ChoreOutcome.Success);
        gemMineResult.GemsCollected.Should().Be(150);
    }

    [Fact]
    public async Task CollectGemMine_WithNoGems_ReportsZeroGemsAndSuccess()
    {
        // Arrange
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        _gamePort.SetChoreResult(_chore1, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(_chore2, new ChoreExecutionResult(true, null, 0));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(_account, _logDirectory);

        // Assert
        run.SuccessCount.Should().Be(2);
        var gemMineResult = run.Results.First(r => r.Chore.Type == ChoreType.CollectGemMine);
        gemMineResult.Outcome.Should().Be(ChoreOutcome.Success);
        gemMineResult.GemsCollected.Should().Be(0);
    }
}