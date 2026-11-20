namespace Raider.Tests.Orchestration;

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

    private readonly Account _alice;
    private readonly Account _bob;
    private readonly Account _carol;

    public MultiAccountRunTests()
    {
        _orchestrator = TestDataBuilder.CreateOrchestrator(_gamePort, _snapshotService, _notificationService);
        _gamePort.SetGameBuild(TestDataBuilder.CreateGameBuild());

        var playtime = TestDataBuilder.CreateChore("playtime", "Claim playtime rewards", ChoreType.ClaimPlaytimeRewards);
        var gemMine = TestDataBuilder.CreateChore("gem-mine", "Collect Gem Mine", ChoreType.CollectGemMine);

        _alice = TestDataBuilder.CreateAccount("alice", "alice@example.com", "Alice", TestDataBuilder.CreateRoutine(playtime, gemMine));
        _bob = TestDataBuilder.CreateAccount("bob", "bob@example.com", "Bob", TestDataBuilder.CreateRoutine(gemMine));
        _carol = TestDataBuilder.CreateAccount("carol", "carol@example.com", "Carol", TestDataBuilder.CreateRoutine(playtime));
    }

    [Fact]
    public async Task Run_WhenGameIsAlreadyOnTheAccount_DoesNotSwitch()
    {
        _gamePort.SetCurrentPlariumLogin("alice@example.com");

        var run = await _orchestrator.ExecuteRunAsync(_alice, _logDirectory);

        _gamePort.SwitchedTo.Should().BeEmpty();
        run.Status.Should().Be(RunStatus.Completed);
        run.Results.Should().HaveCount(2);
    }

    [Fact]
    public async Task Run_MatchesCurrentLoginCaseInsensitively()
    {
        _gamePort.SetCurrentPlariumLogin("ALICE@example.com");

        await _orchestrator.ExecuteRunAsync(_alice, _logDirectory);

        _gamePort.SwitchedTo.Should().BeEmpty();
    }

    [Fact]
    public async Task Run_WhenGameIsOnAnotherAccount_SwitchesBeforeAnyChore()
    {
        _gamePort.SetCurrentPlariumLogin("bob@example.com");

        await _orchestrator.ExecuteRunAsync(_alice, _logDirectory);

        _gamePort.CallLog.Should().Equal(
            "switch:alice@example.com",
            "chore:alice@example.com:playtime",
            "chore:alice@example.com:gem-mine");
    }

    [Fact]
    public async Task Run_WhenCurrentAccountIsUnknown_Switches()
    {
        _gamePort.SetCurrentPlariumLogin(null);

        await _orchestrator.ExecuteRunAsync(_alice, _logDirectory);

        _gamePort.SwitchedTo.Should().Equal("alice@example.com");
    }

    [Fact]
    public async Task RunAll_RunsAccountsSequentiallyInOrder_WithASwitchBetweenThem()
    {
        _gamePort.SetCurrentPlariumLogin("alice@example.com");

        var runs = await _orchestrator.ExecuteRunsAsync(new[] { _alice, _bob, _carol }, _logDirectory);

        runs.Select(r => r.Account.Id).Should().Equal("alice", "bob", "carol");
        runs.Should().OnlyContain(r => r.Status == RunStatus.Completed);
        _gamePort.CallLog.Should().Equal(
            "chore:alice@example.com:playtime",
            "chore:alice@example.com:gem-mine",
            "switch:bob@example.com",
            "chore:bob@example.com:gem-mine",
            "switch:carol@example.com",
            "chore:carol@example.com:playtime");
    }

    [Fact]
    public async Task RunAll_SameAccountTwiceInARow_SwitchesOnlyOnce()
    {
        _gamePort.SetCurrentPlariumLogin(null);

        await _orchestrator.ExecuteRunsAsync(new[] { _alice, _alice }, _logDirectory);

        _gamePort.SwitchedTo.Should().Equal("alice@example.com");
    }

    [Fact]
    public async Task Run_WhenSwitchNeedsPassword_StopsWithoutChoresAndAsksForManualLogin()
    {
        _gamePort.SetCurrentPlariumLogin("alice@example.com");
        _gamePort.SetSwitchResult("bob@example.com", AccountSwitchResult.PasswordRequired());

        var run = await _orchestrator.ExecuteRunAsync(_bob, _logDirectory);

        run.Status.Should().Be(RunStatus.NeedsManualLogin);
        run.NeedsManualLogin.Should().BeTrue();
        run.Results.Should().BeEmpty();
        _gamePort.CallLog.Should().Equal("switch:bob@example.com");
        _notificationService.ManualLoginRequests.Should().ContainSingle().Which.Should().Be(_bob);
        _notificationService.NotificationCount.Should().Be(0, "a manual login is a request to the player, not a Chore failure");
    }

    [Fact]
    public async Task RunAll_WhenOneSwitchNeedsPassword_RemainingAccountsContinue()
    {
        _gamePort.SetCurrentPlariumLogin("alice@example.com");
        _gamePort.SetSwitchResult("bob@example.com", AccountSwitchResult.PasswordRequired());

        var runs = await _orchestrator.ExecuteRunsAsync(new[] { _alice, _bob, _carol }, _logDirectory);

        runs.Select(r => (r.Account.Id, r.Status)).Should().Equal(
            ("alice", RunStatus.Completed),
            ("bob", RunStatus.NeedsManualLogin),
            ("carol", RunStatus.Completed));
        _gamePort.CallLog.Should().Equal(
            "chore:alice@example.com:playtime",
            "chore:alice@example.com:gem-mine",
            "switch:bob@example.com",
            "switch:carol@example.com",
            "chore:carol@example.com:playtime");
        _notificationService.ManualLoginRequests.Should().ContainSingle().Which.Should().Be(_bob);
    }

    [Fact]
    public async Task Run_WhenSwitchFailsForAnotherReason_StopsAndReportsFailure()
    {
        _gamePort.SetCurrentPlariumLogin("alice@example.com");
        _gamePort.SetSwitchResult("bob@example.com", AccountSwitchResult.Failed("Plarium Play is not running"));

        var run = await _orchestrator.ExecuteRunAsync(_bob, _logDirectory);

        run.Status.Should().Be(RunStatus.SwitchFailed);
        run.StatusMessage.Should().Be("Plarium Play is not running");
        run.HasFailures.Should().BeTrue();
        run.Results.Should().BeEmpty();
        _notificationService.NotificationCount.Should().Be(1);
        _notificationService.ManualLoginRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAll_WritesEachAccountsLogsToItsOwnFolder()
    {
        var runs = await _orchestrator.ExecuteRunsAsync(new[] { _alice, _bob }, _logDirectory);

        runs[0].LogDirectory.Should().Be(Path.Combine(_logDirectory, "alice"));
        runs[1].LogDirectory.Should().Be(Path.Combine(_logDirectory, "bob"));
    }

    [Fact]
    public async Task RunAll_WhenCancelled_StopsBeforeTheNextAccount()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => _orchestrator.ExecuteRunsAsync(new[] { _alice, _bob }, _logDirectory, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _gamePort.CallLog.Should().BeEmpty();
    }
}
