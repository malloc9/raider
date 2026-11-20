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

    /// <summary>
    /// Asks the player to log in to an Account by hand because its remembered login needs a password.
    /// </summary>
    Task ShowManualLoginRequiredNotificationAsync(Account account, CancellationToken cancellationToken = default);
}