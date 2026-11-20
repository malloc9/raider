namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Collect Inbox chore outcomes.
/// </summary>
public sealed class CollectInboxChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public CollectInboxChoreTests()
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
    public async Task CollectInbox_EmptyInbox_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var collectInboxChore = TestDataBuilder.CreateChore("collect-inbox-1", "Collect Inbox", ChoreType.CollectInbox);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(collectInboxChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "InboxEmpty"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "InboxEmpty"
        };
        _gamePort.SetChoreResult(collectInboxChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.CollectInbox);
    }

    [Fact]
    public async Task CollectInbox_NoAutoCollectibleItems_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var collectInboxChore = TestDataBuilder.CreateChore("collect-inbox-1", "Collect Inbox", ChoreType.CollectInbox);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(collectInboxChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoAutoCollectibleItems"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoAutoCollectibleItems"
        };
        _gamePort.SetChoreResult(collectInboxChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.CollectInbox);
    }

    [Fact]
    public async Task CollectInbox_Success_ReturnsSuccessOutcomeWithCollectedItems()
    {
        // Arrange
        var collectInboxChore = TestDataBuilder.CreateChore("collect-inbox-1", "Collect Inbox", ChoreType.CollectInbox);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(collectInboxChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with collected items
        var pluginResponse = new
        {
            outcome = "Success",
            collected = new[]
            {
                new { itemId = "item-1", itemType = "Silver", rewards = (object)new { silver = 10000 } },
                new { itemId = "item-2", itemType = "Energy", rewards = (object)new { energy = 50 } },
                new { itemId = "item-3", itemType = "Shard", rewards = (object)new { ancientShard = 1 } }
            },
            leftInInbox = 2
        };
        _gamePort.SetChoreResult(collectInboxChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.CollectInbox);
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task CollectInbox_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var collectInboxChore = TestDataBuilder.CreateChore("collect-inbox-1", "Collect Inbox", ChoreType.CollectInbox);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(collectInboxChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(collectInboxChore, new ChoreExecutionResult(false, "Failed to open inbox"));

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
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.CollectInbox);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingCollectInbox_SummaryShowsAllResults()
    {
        // Arrange
        var collectInboxChore = TestDataBuilder.CreateChore("collect-inbox-1", "Collect Inbox", ChoreType.CollectInbox);
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(collectInboxChore, clanCheckInChore, playtimeChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Collect Inbox: success with collected items
        var collectInboxResponse = new
        {
            outcome = "Success",
            collected = new[]
            {
                new { itemId = "item-1", itemType = "Silver", rewards = (object)new { silver = 10000 } }
            },
            leftInInbox = 1
        };
        _gamePort.SetChoreResult(collectInboxChore, new ChoreExecutionResult(true, Data: collectInboxResponse));
        
        // Clan check-in: already checked in (nothing to do)
        var clanCheckInResponse = new
        {
            outcome = "NothingToDo",
            reason = "AlreadyCheckedIn"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: clanCheckInResponse));
        
        // Playtime rewards: success
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(3);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(3);
        
        // Verify all chore types are in results
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.CollectInbox);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClanCheckIn);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        
        // Verify Collect Inbox result has data with collected items
        var collectInboxResult = run.Results.First(r => r.Chore.Type == ChoreType.CollectInbox);
        collectInboxResult.Data.Should().NotBeNull();
    }
}