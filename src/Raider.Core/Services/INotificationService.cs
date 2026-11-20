namespace Raider.Core.Services;

using Raider.Core.Models;

/// <summary>
/// Service for showing Windows notifications.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a notification for a completed Run if it had failures.
    /// </summary>
    /// <param name="run">The completed run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ShowRunCompletionNotificationAsync(Run run, CancellationToken cancellationToken = default);
}