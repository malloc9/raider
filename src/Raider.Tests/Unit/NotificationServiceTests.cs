namespace Raider.Tests.Unit;

using Raider.Core.Models;
using Raider.Core.Services;
using Raider.Tests.Fakes;
using Xunit;
using FluentAssertions;

/// <summary>
/// Unit tests for notification service behavior.
/// </summary>
public sealed class NotificationServiceTests
{
    [Fact]
    public async Task FakeNotificationService_TracksNotifications()
    {
        // Arrange
        var service = new FakeNotificationService();
        var run = CreateSampleRun(hasFailures: true);

        // Act
        await service.ShowRunCompletionNotificationAsync(run);

        // Assert
        service.NotificationCount.Should().Be(1);
        service.LastNotifiedRun.Should().Be(run);
    }

    [Fact]
    public async Task FakeNotificationService_Reset_ClearsState()
    {
        // Arrange
        var service = new FakeNotificationService();
        var run = CreateSampleRun(hasFailures: true);
        await service.ShowRunCompletionNotificationAsync(run);

        // Act
        service.Reset();

        // Assert
        service.NotificationCount.Should().Be(0);
        service.LastNotifiedRun.Should().BeNull();
    }

    [Fact]
    public async Task FakeNotificationService_MultipleCalls_IncrementsCount()
    {
        // Arrange
        var service = new FakeNotificationService();
        var run1 = CreateSampleRun(hasFailures: true);
        var run2 = CreateSampleRun(hasFailures: true);

        // Act
        await service.ShowRunCompletionNotificationAsync(run1);
        await service.ShowRunCompletionNotificationAsync(run2);

        // Assert
        service.NotificationCount.Should().Be(2);
        service.LastNotifiedRun.Should().Be(run2);
    }

    private static Run CreateSampleRun(bool hasFailures)
    {
        var account = TestDataBuilder.CreateAccount();
        var chore = TestDataBuilder.CreateChore("chore-1", "Test");
        var routine = TestDataBuilder.CreateRoutine(chore);
        var accountWithRoutine = account with { Routine = routine };
        
        var result = hasFailures
            ? ChoreResult.Failed(chore, "Error", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
            : ChoreResult.Success(chore, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        return new Run(
            "run-1",
            accountWithRoutine,
            TestDataBuilder.CreateGameBuild(),
            TestDataBuilder.CreateGameBuild(),
            new[] { result },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "/tmp/logs");
    }
}