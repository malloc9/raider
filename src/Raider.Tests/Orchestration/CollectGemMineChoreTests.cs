namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Collect Gem Mine chore outcomes.
/// </summary>
public sealed class CollectGemMineChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public CollectGemMineChoreTests()
    {
        _gamePort = new FakeGamePort();
        _snapshotService = new FakeSnapshotService();
        _notificationService = new FakeNotificationService();
        _logDirectory = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
        
        _orchestrator = TestDataBuilder.CreateOrchestrator(
            _gamePort,
            _snapshotService,
            _notificationService);

        Directory.CreateDirectory(_logDirectory);
    }

    [Fact]
    public async Task CollectGemMine_NoGemsAccumulated_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoGemsAccumulated"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoGemsAccumulated",
            gemsCollected = 0
        };
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.CollectGemMine);
        result.GemsCollected.Should().Be(0);
    }

    [Fact]
    public async Task CollectGemMine_Success_ReturnsSuccessOutcomeWithGemsCollected()
    {
        // Arrange
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with gems collected
        var pluginResponse = new
        {
            outcome = "Success",
            gemsCollected = 150
        };
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(true, Data: pluginResponse, GemsCollected: 150));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.CollectGemMine);
        result.GemsCollected.Should().Be(150);
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task CollectGemMine_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(false, "Failed to collect Gem Mine"));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeTrue();
        run.SuccessCount.Should().Be(0);
        run.FailureCount.Should().Be(1);
        
        // Notification should be raised
        _notificationService.NotificationCount.Should().Be(1);
        
        // Snapshot should be saved
        _snapshotService.SnapshotCount.Should().Be(1);
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.CollectGemMine);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingCollectGemMine_SummaryShowsAllResults()
    {
        // Arrange
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(gemMineChore, playtimeChore, clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Gem Mine: no gems accumulated (nothing to do)
        var gemMineResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoGemsAccumulated",
            gemsCollected = 0
        };
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(true, Data: gemMineResponse));
        
        // Playtime rewards: success
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true));
        
        // Clan check-in: already checked in (nothing to do)
        var clanCheckInResponse = new
        {
            outcome = "NothingToDo",
            reason = "AlreadyCheckedIn"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: clanCheckInResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(3);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(3);
        
        // Verify all chore types are in results
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.CollectGemMine);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClanCheckIn);
        
        // Verify Collect Gem Mine result has data
        var gemMineResult = run.Results.First(r => r.Chore.Type == ChoreType.CollectGemMine);
        gemMineResult.Data.Should().NotBeNull();
    }
}