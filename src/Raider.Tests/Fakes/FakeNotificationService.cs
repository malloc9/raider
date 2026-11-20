namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Services;

/// <summary>
/// Fake implementation of INotificationService for testing.
/// </summary>
public sealed class FakeNotificationService : INotificationService
{
    public Run? LastNotifiedRun { get; private set; }
    public int NotificationCount { get; private set; }

    public Task ShowRunCompletionNotificationAsync(Run run, CancellationToken cancellationToken = default)
    {
        LastNotifiedRun = run;
        NotificationCount++;
        return Task.CompletedTask;
    }

    public void Reset()
    {
        LastNotifiedRun = null;
        NotificationCount = 0;
    }
}