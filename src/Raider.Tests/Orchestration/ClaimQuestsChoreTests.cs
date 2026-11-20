namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Claim Quests chore outcomes.
/// </summary>
public sealed class ClaimQuestsChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public ClaimQuestsChoreTests()
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
    public async Task ClaimQuests_NoQuestRewards_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(questsChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoQuestRewards"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoQuestRewards"
        };
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimQuests);
    }

    [Fact]
    public async Task ClaimQuests_NoCompletedRewards_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(questsChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoCompletedRewards"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoCompletedRewards"
        };
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimQuests);
    }

    [Fact]
    public async Task ClaimQuests_Success_ReturnsSuccessOutcomeWithClaimedRewards()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(questsChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with claimed rewards
        var pluginResponse = new
        {
            outcome = "Success",
            claimed = new[]
            {
                new { rewardId = "daily-1", questType = "Daily", tab = "Daily", rewards = new { energy = 30, silver = 5000 } },
                new { rewardId = "weekly-1", questType = "Weekly", tab = "Weekly", rewards = new { ancientShard = 1 } },
                new { rewardId = "monthly-1", questType = "Monthly", tab = "Monthly", rewards = new { voidShard = 1 } },
                new { rewardId = "advanced-1", questType = "Advanced", tab = "Advanced", rewards = new { sacredShard = 1 } },
                new { rewardId = "daily-progress", questType = "ProgressChest", tab = "Daily", rewards = new { energy = 100, silver = 25000 } },
                new { rewardId = "weekly-progress", questType = "ProgressChest", tab = "Weekly", rewards = new { ancientShard = 2, voidShard = 1 } }
            },
            remaining = 3
        };
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimQuests);
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ClaimQuests_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(questsChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(false, "Failed to open quests screen"));

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
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.ClaimQuests);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingClaimQuests_SummaryShowsAllResults()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(questsChore, playtimeChore, clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Quests: success with claimed items
        var questsResponse = new
        {
            outcome = "Success",
            claimed = new[]
            {
                new { rewardId = "daily-1", questType = "Daily", tab = "Daily", rewards = new { energy = 30 } }
            },
            remaining = 2
        };
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(true, Data: questsResponse));
        
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
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimQuests);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClanCheckIn);
        
        // Verify Claim Quests result has data with claimed rewards
        var questsResult = run.Results.First(r => r.Chore.Type == ChoreType.ClaimQuests);
        questsResult.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task Run_WithNoCompletedRewards_SummaryShowsNothingToDoNotFailure()
    {
        // Arrange
        var questsChore = TestDataBuilder.CreateChore("quests-1", "Claim Quests", ChoreType.ClaimQuests);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(questsChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoCompletedRewards"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoCompletedRewards"
        };
        _gamePort.SetChoreResult(questsChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse("With no completed rewards, the summary should say 'nothing to do', not failure");
        run.SuccessCount.Should().Be(1);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClaimQuests);
    }
}