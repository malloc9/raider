namespace Raider.Tests.Orchestration;

using System.IO;
using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Tests for the Buy Free Market Items chore outcomes.
/// </summary>
public sealed class BuyFreeMarketItemsChoreTests
{
    private readonly FakeGamePort _gamePort;
    private readonly FakeSnapshotService _snapshotService;
    private readonly FakeNotificationService _notificationService;
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory;

    public BuyFreeMarketItemsChoreTests()
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
    public async Task BuyFreeMarketItems_NoMarketItems_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("market-1", "Buy Free Market Items", ChoreType.BuyFreeMarketItems);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(chore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoMarketItems"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoMarketItems"
        };
        _gamePort.SetChoreResult(chore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.BuyFreeMarketItems);
    }

    [Fact]
    public async Task BuyFreeMarketItems_NoFreeItems_ReturnsNothingToDoWithReason()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("market-1", "Buy Free Market Items", ChoreType.BuyFreeMarketItems);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(chore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns "NothingToDo" with reason "NoFreeItems"
        var pluginResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoFreeItems"
        };
        _gamePort.SetChoreResult(chore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.BuyFreeMarketItems);
    }

    [Fact]
    public async Task BuyFreeMarketItems_Success_ReturnsSuccessOutcomeWithTakenItems()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("market-1", "Buy Free Market Items", ChoreType.BuyFreeMarketItems);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(chore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns success with taken items
        var pluginResponse = new
        {
            outcome = "Success",
            taken = new[]
            {
                new { itemId = "energy_small", itemName = "Small Energy Refill", quantity = 2 },
                new { itemId = "silver_50k", itemName = "50,000 Silver", quantity = 1 }
            }
        };
        _gamePort.SetChoreResult(chore, new ChoreExecutionResult(true, Data: pluginResponse));

        // Act
        var run = await _orchestrator.ExecuteRunAsync(account, _logDirectory);

        // Assert
        run.HasFailures.Should().BeFalse();
        run.SuccessCount.Should().Be(1);
        run.Results.Should().HaveCount(1);
        
        var result = run.Results[0];
        result.Outcome.Should().Be(ChoreOutcome.Success);
        result.Chore.Type.Should().Be(ChoreType.BuyFreeMarketItems);
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task BuyFreeMarketItems_Failure_RaisesNotificationAndSnapshot()
    {
        // Arrange
        var chore = TestDataBuilder.CreateChore("market-1", "Buy Free Market Items", ChoreType.BuyFreeMarketItems);
        var account = TestDataBuilder.CreateAccount(routine: TestDataBuilder.CreateRoutine(chore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Plugin returns failure
        _gamePort.SetChoreResult(chore, new ChoreExecutionResult(false, "Failed to access market"));

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
        _snapshotService.SavedSnapshots[0].Result.Chore.Type.Should().Be(ChoreType.BuyFreeMarketItems);
        _snapshotService.SavedSnapshots[0].Result.Outcome.Should().Be(ChoreOutcome.Failed);
    }

    [Fact]
    public async Task Run_WithMultipleChoresIncludingBuyFreeMarketItems_SummaryShowsAllResults()
    {
        // Arrange
        var marketChore = TestDataBuilder.CreateChore("market-1", "Buy Free Market Items", ChoreType.BuyFreeMarketItems);
        var playtimeChore = TestDataBuilder.CreateChore("playtime-1", "Claim Playtime Rewards", ChoreType.ClaimPlaytimeRewards);
        var gemMineChore = TestDataBuilder.CreateChore("gemmine-1", "Collect Gem Mine", ChoreType.CollectGemMine);
        
        var account = TestDataBuilder.CreateAccount(
            routine: TestDataBuilder.CreateRoutine(marketChore, playtimeChore, gemMineChore));
        
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild("1.0.0"));
        
        // Market: no free items (nothing to do)
        var marketResponse = new
        {
            outcome = "NothingToDo",
            reason = "NoFreeItems"
        };
        _gamePort.SetChoreResult(marketChore, new ChoreExecutionResult(true, Data: marketResponse));
        
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
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.BuyFreeMarketItems);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.ClaimPlaytimeRewards);
        run.Results.Select(r => r.Chore.Type).Should().Contain(ChoreType.CollectGemMine);
    }
}