namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Clan Check-In chore outcomes.
/// </summary>
public sealed class ClanCheckInChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public ClanCheckInChoreTests()
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
    public async Task ClanCheckIn_NotInClan_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NotInClan"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NotInClan"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClanCheckIn);
    }

    [Fact]
    public async Task ClanCheckIn_AlreadyCheckedIn_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "AlreadyCheckedIn"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "AlreadyCheckedIn"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClanCheckIn);
    }

    [Fact]
    public async Task ClanCheckIn_Success_ReturnsSuccessOutcome()
    {
        // Arrange
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with rewards
        var pluginResponse = new
        {
            outcome = "Success",
            rewards = new { clanXp = 100, silver = 5000 }
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.ClanCheckIn);
    }

    [Fact]
    public async Task ClanCheckIn_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(clanCheckInChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(false, "Failed to find check-in button"));

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
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.ClanCheckIn);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingClanCheckIn_SummaryShowsAllResults()
    {
        // Arrange
        var clanCheckInChore = TestDataBuilder.CreateChore("clan-checkin-1", "Clan Check-In", ChoreType.ClanCheckIn);
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(clanCheckInChore, playtimeChore, gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Clan check-in: already checked in (nothing to do)
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "AlreadyCheckedIn"
        };
        _gamePort.SetChoreResult(clanCheckInChore, new ChoreExecutionResult(true, Data: pluginResponse));
        
        // Other chores succeed
        _gamePort.SetChoreResult(playtimeChore, new ChoreExecutionResult(true));
        _gamePort.SetChoreResult(gemMineChore, new ChoreExecutionResult(true));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(3);
        run.FailureCount.Should().Be(0);
        run.Results.Should().HaveCount(3);
        
        // Verify all chore types are in results
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClanCheckIn);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.CollectGemMine);
    }
}