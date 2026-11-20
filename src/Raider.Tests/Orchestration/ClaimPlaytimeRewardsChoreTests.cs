namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Claim Playtime Rewards chore outcomes.
/// </summary>
public sealed class ClaimPlaytimeRewardsChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public ClaimPlaytimeRewardsChoreTests()
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
    public async Task ClaimPlaytimeRewards_NoPlaytimeRewards_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoPlaytimeRewards"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoPlaytimeRewards"
        };
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimPlaytimeRewards);
    }

    [Fact]
    public async Task ClaimPlaytimeRewards_NoFinishedGifts_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoFinishedGifts"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoFinishedGifts"
        };
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimPlaytimeRewards);
    }

    [Fact]
    public async Task ClaimPlaytimeRewards_Success_ReturnsSuccessOutcomeWithClaimedRewards()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with claimed rewards
        var pluginResponse = new
        {
            outcome = "Success",
            claimed = new[]
            {
                new { rewardId = "gift-1", rewardType = "Energy", rewards = new { energy = 30 } },
                new { rewardId = "gift-2", rewardType = "Silver", rewards = new { silver = 50000 } },
                new { rewardId = "gift-3", rewardType = "Mystery Shard", rewards = new { mysteryShard = 1 } }
            },
            remaining = 2
        };
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimPlaytimeRewards);
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimPlaytimeRewards_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(false, "Failed to open playtime rewards screen"));

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
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.ClaimPlaytimeRewards);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingClaimPlaytimeRewards_SummaryShowsAllResults()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(playtimeChore, clanCheckInChore, gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Playtime rewards: success with claimed items
        var playtimeResponse = new
        {
            outcome = "Success",
            claimed = new[]
            {
                new { rewardId = "gift-1", rewardType = "Energy", rewards = new { energy = 30 } }
            },
            remaining = 1
        };
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true, Data: playtimeResponse));
        
        // Clan check-in: already checked in (nothing to do)
        var clanCheckInResponse = new
        {
            outcome = "NothingToDo",
            reason = "AlreadyCheckedIn"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: clanCheckInResponse));
        
        // Gem mine: success
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(true));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(3);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(3);
        
        // Verify all chore types are in results
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClanCheckIn);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.CollectGemMine);
        
        // Verify Claim Playtime Rewards result has data with claimed rewards
        var playtimeResult = run.Results.First(r => r.Chore.Type == ChoreType.ClaimPlaytimeRewards);
        playtimeResult.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task Run_WithNoFinishedGifts_SummaryShowsNothingToDoNotFailure()
    {
        // Arrange
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoFinishedGifts"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoFinishedGifts"
        };
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse("With no finished gifts, the summary should say 'nothing to do', not failure");
        run.SuccessCount.Should().Be(1);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimPlaytimeRewards);
    }
}