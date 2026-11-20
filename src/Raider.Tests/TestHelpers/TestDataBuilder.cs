namespace Raider.Tests.TestHelpers;

using Raider.Core.Models;

/// <summary>
/// Helper for building test data.
/// </summary>
public static class TestDataBuilder
{
    public static Chore CreateChore(string id, string name, ChoreType type = ChoreType.ClaimPlaytimeRewards)
        => new(id, name, type);

    public static Routine CreateRoutine(params Chore[] chores)
        => new(chores.ToList().AsReadOnly());

    public static Account CreateAccount(
        string id = "account-1",
        string displayName = "Test Account",
        Routine? routine = null,
        int lastRunGameDay = 0,
        RunOutcome lastOutcome = RunOutcome.Completed)
        => new(id, displayName, routine ?? CreateRoutine(), lastRunGameDay, lastOutcome);

    public static GameBuild CreateGameBuild(string version = "1.0.0")
        => new(version, DateTimeOffset.UtcNow);

    public static Instance CreateInstance(
        string id = "instance-1",
        string token = "token-1",
        int processId = 1234,
        string? playerId = "player-1",
        string? playerName = "Test Player",
        InstanceStatus status = InstanceStatus.Ready)
        => new(id, token, processId, playerId, playerName, DateTimeOffset.UtcNow, status);

    public static RunOrchestrator CreateOrchestrator(
        FakeGamePort gamePort,
        FakeSnapshotService snapshotService,
        FakeNotificationService notificationService,
        GameBuild? targetedGameBuild = null)
    {
        var logger = new FakeLogger<RunOrchestrator>();
        return new RunOrchestrator(
            gamePort,
            snapshotService,
            notificationService,
            targetedGameBuild ?? CreateGameBuild(),
            logger);
    }
}

/// <summary>
/// Simple fake logger for testing.
/// </summary>
public sealed class FakeLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Logs { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Logs.Add((logLevel, formatter(state, exception)));
    }
}