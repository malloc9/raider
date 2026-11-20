namespace Raider.Tests.Orchestration;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Raider.Tests.TestHelpers;
using Xunit;
using FluentAssertions;

/// <summary>
/// Orchestration tests against a fake Game port covering several Accounts: switching only when needed,
/// sequential order, and stopping an Account's Run when its switch needs a password.
/// </summary>
public sealed class MultiAccountRunTests
{
    private readonly FakeGamePort _gamePort = new();
    private readonly FakeSnapshotService _snapshotService = new();
    private readonly FakeNotificationService _notificationService = new();
    private readonly RunOrchestrator _orchestrator;
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), "raider-tests", Guid.NewGuid().ToString());
    private const string InstanceToken1 = "token-1";
    private const string InstanceToken2 = "token-2";
    private const string InstanceToken3 = "token-3";

    private readonly Account _alice;
    private readonly Account _bob;
    private readonly Account _carol;

    public MultiAccountRunTests()
    {
        _orchestrator = TestDataBuilder.CreateOrchestrator(_gamePort, _snapshotService, _notificationService);
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild());

        var playtime = TestDataBuilder.CreateChore("playtime", "Claim playtime rewards", ChoreType.ClaimPlaytimeRewards);
        var gemMine = TestDataBuilder.CreateChore("gem-mine", "Collect Gem Mine", ChoreType.CollectGemMine);

        _alice = TestDataBuilder.CreateAccount("alice", "Alice", TestDataBuilder.CreateRoutine(playtime, gemMine));
        _bob = TestDataBuilder.CreateAccount("bob", "Bob", TestDataBuilder.CreateRoutine(gemMine));
        _carol = TestDataBuilder.CreateAccount("carol", "Carol", TestDataBuilder.CreateRoutine(playtime));
    }

    [Fact]
    public async Task Run_WhenGameIsAlreadyOnTheAccount_DoesNotSwitch()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, "alice");

        var run = await _orchestrator.ExecuteRunAsync(_alice, InstanceToken1, _logDirectory);

        _gamePort.SwitchedTo[InstanceToken1].Should().BeEmpty();
        run.Status.Should().Be(RunStatus.Completed);
        run.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Run_MatchesCurrentLoginCaseInsensitively()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, "ALICE");

        await _orchestrator.ExecuteRunAsync(_alice, InstanceToken1, _logDirectory);

        _gamePort.SwitchedTo[InstanceToken1].Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WhenGameIsOnAnotherAccount_SwitchesBeforeAnyChore()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, "bob");

        await _orchestrator.ExecuteRunAsync(_alice, InstanceToken1, _logDirectory);

        _gamePort.CallLog.Should().Equal(
            $"switch:{InstanceToken1}:alice",
            $"chore:{InstanceToken1}:alice:playtime",
            $"chore:{InstanceToken1}:alice:gem-mine");
    }

    [Fact]
    public async Task Run_WhenCurrentAccountIsUnknown_Switches()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, null);

        await _orchestrator.ExecuteRunAsync(_alice, InstanceToken1, _logDirectory);

        _gamePort.SwitchedTo[InstanceToken1].Should().Equal("alice");
    }

    [Fact]
    public async Task Run_WhenSwitchNeedsPassword_StopsWithoutChoresAndAsksForManualLogin()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, "alice");
        _gamePort.SetSwitchResult(InstanceToken1, "bob", AccountSwitchResult.PasswordRequired());

        var run = await _orchestrator.ExecuteRunAsync(_bob, InstanceToken1, _logDirectory);

        run.Status.Should().Be(RunStatus.NeedsManualLogin);
        run.NeedsManualLogin.Should().BeTrue();
        run.Results.Should().BeEmpty();
        _gamePort.CallLog.Should().Equal($"switch:{InstanceToken1}:bob");
        _notificationService.ManualLoginRequests.Should().ContainSingle().Which.Should().Be(_bob);
        _notificationService.NotificationCount.Should().Be(0, "a manual login is a request to the player, not a Chore failure");
    }

    [Fact]
    public async Task Run_WhenSwitchFailsForAnotherReason_StopsAndReportsFailure()
    {
        _gamePort.SetCurrentPlayerId(InstanceToken1, "alice");
        _gamePort.SetSwitchResult(InstanceToken1, "bob", AccountSwitchResult.Failed("Plarium Play is not running"));

        var run = await _orchestrator.ExecuteRunAsync(_bob, InstanceToken1, _logDirectory);

        run.Status.Should().Be(RunStatus.SwitchFailed);
        run.StatusMessage.Should().Be("Plarium Play is not running");
        run.HasFailures.Should().BeTrue();
        run.Results.Should().BeEmpty();
        _notificationService.NotificationCount.Should().Be(1);
        _notificationService.ManualLoginRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_Cancelled_StopsImmediately()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _orchestrator.ExecuteRunAsync(_alice, InstanceToken1, _logDirectory, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _gamePort.CallLog.Should().BeEmpty();
    }
}