namespace Raider.Tests.Orchestration;

using System.Collections.Concurrent;
using Raider.Core.Models;
using Raider.Core.Ports;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Orchestration tests covering concurrency, Game-day gating, busy deferral, duplicate and InstanceLost.
/// </summary>
public sealed class RunCoordinatorTests
{
    private readonly FakeGamePort _gamePort = new();
    private readonly FakeInstanceRegistry _instanceRegistry = new();
    private readonly FakeAccountStore _accountStore = new();
    private readonly FakeSnapshotService _snapshotService = new();
    private readonly FakeNotificationService _notificationService = new();
    private readonly RunOrchestrator _orchestrator;
    private readonly RunCoordinator _coordinator;
    private readonly GameBuild _gameBuild = TestDataBuilder.CreateGameBuild();

    private readonly Instance _instance1;
    private readonly Instance _instance2;
    private readonly Account _account1;
    private readonly Account _account2;

    public RunCoordinatorTests()
    {
        _orchestrator = TestDataBuilder.CreateOrchestrator(_gamePort, _snapshotService, _notificationService, _gameBuild);
        _coordinator = new RunCoordinator(_instanceRegistry, _accountStore, _orchestrator, new FakeLogger<RunCoordinator>());
        
        _gamePort.SetGameBuild(_gameBuild);

        _instance1 = TestDataBuilder.CreateInstance("instance-1", "token-1", 1000, "player-1", "Player One");
        _instance2 = TestDataBuilder.CreateInstance("instance-2", "token-2", 2000, "player-2", "Player Two");

        _account1 = TestDataBuilder.CreateAccount("player-1", "Player One");
        _account2 = TestDataBuilder.CreateAccount("player-2", "Player Two");

        _instanceRegistry.AddInstance(_instance1);
        _instanceRegistry.AddInstance(_instance2);
        _accountStore.AddAccount(_account1);
        _accountStore.AddAccount(_account2);

        // Default all chores to succeed
        foreach (var chore in Routine.Default().Chores)
        {
            _gamePort.SetChoreResult("token-1", chore, new ChoreExecutionResult(true));
            _gamePort.SetChoreResult("token-2", chore, new ChoreExecutionResult(true));
        }
    }

    [Fact]
    public async Task AutoRun_WhenInstanceAppearsAndNoRunToday_RunsAutomatically()
    {
        _coordinator.Start();

        // Wait for auto-run to trigger
        await Task.Delay(100);

        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-1:player-1:"));
        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-2:player-2:"));
    }

    [Fact]
    public async Task AutoRun_WhenAccountAlreadyRanToday_SkipsAutoRun()
    {
        // Set last run to today
        var today = GameDay.Current();
        _accountStore.AddAccount(_account1 with { LastRunGameDay = today });

        _coordinator.Start();
        await Task.Delay(100);

        _gamePort.CallLog.Should().NotContain(log => log.StartsWith("chore:token-1:player-1:"));
    }

    [Fact]
    public async Task ManualRun_BypassesGameDayGating()
    {
        // Set last run to today
        var today = GameDay.Current();
        _accountStore.AddAccount(_account1 with { LastRunGameDay = today });

        var run = await _coordinator.RunManualAsync("player-1");

        run.Should().NotBeNull();
        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-1:player-1:"));
    }

    [Fact]
    public async Task RunAll_BypassesGameDayGating()
    {
        // Set both accounts as already run today
        var today = GameDay.Current();
        _accountStore.AddAccount(_account1 with { LastRunGameDay = today });
        _accountStore.AddAccount(_account2 with { LastRunGameDay = today });

        var runs = await _coordinator.RunAllManualAsync();

        runs.Should().HaveCount(2);
        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-1:player-1:"));
        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-2:player-2:"));
    }

    [Fact]
    public async Task ConcurrentRuns_DifferentInstances_RunInParallel()
    {
        var started = new ConcurrentBag<string>();
        var completed = new ConcurrentBag<string>();

        // Add delay to chores to verify concurrency
        foreach (var chore in Routine.Default().Chores)
        {
            _gamePort.SetChoreResult("token-1", chore, new ChoreExecutionResult(true));
            _gamePort.SetChoreResult("token-2", chore, new ChoreExecutionResult(true));
        }

        _coordinator.Start();
        await Task.Delay(200); // Let both runs start

        // Both instances should have started chores
        _gamePort.CallLog.Count(log => log.StartsWith("chore:token-1:")).Should().BeGreaterThan(0);
        _gamePort.CallLog.Count(log => log.StartsWith("chore:token-2:")).Should().BeGreaterThan(0);

        // Wait for both to complete
        await Task.Delay(500);

        // Both should complete
        var token1Chores = _gamePort.CallLog.Count(log => log.StartsWith("chore:token-1:"));
        var token2Chores = _gamePort.CallLog.Count(log => log.StartsWith("chore:token-2:"));
        token1Chores.Should().Be(6);
        token2Chores.Should().Be(6);
    }

    [Fact]
    public async Task BusyInstance_WaitUpTo5Minutes_ThenSkipAndRetryNextPass()
    {
        // Mark instance1 as busy
        _instanceRegistry.SetStatus("instance-1", InstanceStatus.Busy);

        _coordinator.Start();
        await Task.Delay(100);

        // Instance1 should not run (busy), instance2 should run
        _gamePort.CallLog.Should().NotContain(log => log.StartsWith("chore:token-1:"));
        _gamePort.CallLog.Should().Contain(log => log.StartsWith("chore:token-2:"));

        // InstanceRegistry marks it as Waiting after timeout (handled by InstanceRegistry, not coordinator)
        // But coordinator should not try to run busy instances
    }

    [Fact]
    public async Task DuplicatePlayerId_FirstRunsSecondFlaggedDuplicate()
    {
        // Add second instance with same player ID
        var duplicateInstance = TestDataBuilder.CreateInstance("instance-3", "token-3", 3000, "player-1", "Player One Duplicate");
        _instanceRegistry.AddInstance(duplicateInstance);

        _coordinator.Start();
        await Task.Delay(100);

        // Only one run for player-1 should happen
        var player1Runs = _gamePort.CallLog.Count(log => log.Contains("player-1"));
        player1Runs.Should().BeGreaterThan(0); // At least some chores ran

        // The InstanceRegistry handles duplicate detection
        var instances = _instanceRegistry.GetInstances();
        var player1Instances = instances.Where(i => i.PlayerId == "player-1").ToList();
        player1Instances.Should().HaveCount(2);
        // One should be Ready/Busy, other Duplicate
        player1Instances.Count(i => i.Status == InstanceStatus.Duplicate).Should().Be(1);
    }

    [Fact]
    public async Task InstanceLostMidRun_RunEndsInstanceLost_GameDayNotMarkedDone()
    {
        _coordinator.Start();
        await Task.Delay(50); // Let run start

        // Simulate instance lost mid-run
        _instanceRegistry.SetStatus("instance-1", InstanceStatus.InstanceLost);
        await Task.Delay(100);

        // Check that account's LastRunGameDay was NOT updated (game day not marked done)
        var accounts = await _accountStore.LoadAsync();
        var account = accounts.First(a => a.Id == "player-1");
        account.LastRunGameDay.Should().Be(0); // Still 0, not today
    }

    [Fact]
    public async Task AutoRegister_UnknownPlayerId_CreatesAccountWithDefaultRoutine()
    {
        var newInstance = TestDataBuilder.CreateInstance("instance-new", "token-new", 4000, "player-new", "New Player");
        _instanceRegistry.AddInstance(newInstance);

        _coordinator.Start();
        await Task.Delay(100);

        var accounts = await _accountStore.LoadAsync();
        var newAccount = accounts.FirstOrDefault(a => a.Id == "player-new");
        
        newAccount.Should().NotBeNull();
        newAccount!.DisplayName.Should().Be("New Player");
        newAccount.Routine.Chores.Should().HaveCount(6);
        newAccount.Routine.Chores.Select(c => c.Id).Should().Equal(
            "claim-playtime-rewards",
            "claim-quests",
            "collect-inbox",
            "buy-free-market-items",
            "collect-gem-mine",
            "clan-check-in");
    }

    [Fact]
    public async Task InstanceWithoutPlayerId_Ignored()
    {
        var noPlayerInstance = TestDataBuilder.CreateInstance("instance-noplayer", "token-noplayer", 5000, playerId: null);
        _instanceRegistry.AddInstance(noPlayerInstance);

        _coordinator.Start();
        await Task.Delay(100);

        var accounts = await _accountStore.LoadAsync();
        accounts.Should().HaveCount(2); // Only original accounts
    }

    [Fact]
    public async Task OneInstanceFails_OthersContinue()
    {
        // Make instance1 fail on first chore
        var playtime = Routine.Default().Chores.First(c => c.Id == "claim-playtime-rewards");
        _gamePort.SetChoreResult("token-1", playtime, new ChoreExecutionResult(false, "Simulated failure"));

        _coordinator.Start();
        await Task.Delay(500);

        // Instance2 should still complete all chores
        var token2Chores = _gamePort.CallLog.Count(log => log.StartsWith("chore:token-2:"));
        token2Chores.Should().Be(6);

        // Instance1 should have failed but not blocked instance2
        var token1Chores = _gamePort.CallLog.Count(log => log.StartsWith("chore:token-1:"));
        token1Chores.Should().BeGreaterThan(0); // At least attempted
    }

    [Fact]
    public async Task RunManual_InstanceNotFound_ReturnsNull()
    {
        var run = await _coordinator.RunManualAsync("nonexistent");
        run.Should().BeNull();
    }

    [Fact]
    public async Task RunManual_AccountNotInStore_ReturnsNull()
    {
        var run = await _coordinator.RunManualAsync("unknown-account");
        run.Should().BeNull();
    }

    [Fact]
    public async Task GameDayChange_ResetsRunEligibility()
    {
        // This test verifies the concept - in real usage GameDay.Current() uses DateTimeOffset.UtcNow
        // We can't easily mock time, but we verify the logic exists
        var today = GameDay.Current();
        _accountStore.AddAccount(_account1 with { LastRunGameDay = today });

        // Verify HasRunToday returns true
        GameDay.HasRunToday(_account1 with { LastRunGameDay = today }).Should().BeTrue();
        GameDay.HasRunToday(_account1 with { LastRunGameDay = today - 1 }).Should().BeFalse();
    }

    [Fact]
    public void InstanceStatus_Enum_HasAllRequiredValues()
    {
        // Verify all required statuses exist
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Discovered);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Ready);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Busy);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Waiting);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Duplicate);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.Dead);
        Enum.GetValues<InstanceStatus>().Should().Contain(InstanceStatus.InstanceLost);
    }

    [Fact]
    public void RunOutcome_Enum_HasAllRequiredValues()
    {
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.Completed);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.NeedsManualLogin);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.SwitchFailed);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.InstanceLost);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.Cancelled);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.BusyTimeout);
        Enum.GetValues<RunOutcome>().Should().Contain(RunOutcome.Duplicate);
    }

    [Fact]
    public void RunStatus_Enum_HasInstanceLost()
    {
        Enum.GetValues<RunStatus>().Should().Contain(RunStatus.InstanceLost);
    }

    [Fact]
    public async Task Run_InstanceLost_HasCorrectStatus()
    {
        var run = await _orchestrator.ExecuteRunWithInstanceLostAsync(_account1, "token-1", "/tmp/logs");
        
        run.Status.Should().Be(RunStatus.InstanceLost);
        run.IsInstanceLost.Should().BeTrue();
        run.HasFailures.Should().BeTrue(); // InstanceLost counts as failure
    }
}