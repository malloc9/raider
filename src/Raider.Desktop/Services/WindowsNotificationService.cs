namespace Raider.Desktop.Services;

using Raider.Core.Models;
using Raider.Core.Services;
using Microsoft.Toolkit.Uwp.Notifications;

/// <summary>
/// Windows implementation of INotificationService using toast notifications.
/// </summary>
public sealed class WindowsNotificationService : INotificationService
{
    public async Task ShowRunCompletionNotificationAsync(Run run, CancellationToken cancellationToken = default)
    {
        // Only show notification if there are failures (already checked by caller, but defensive)
        if (!run.HasFailures)
        {
            return;
        }

        var title = $"Raider: Run completed with failures";
        var body = $"Account {run.Account.DisplayName}: {run.FailureCount} chore(s) failed";

        if (run.IsGameBuildMismatch)
        {
            body += $" ({run.BrokenByGameUpdateCount} broken by game update)";
        }

        // Build toast content
        var toastContent = new ToastContent
        {
            Visual = new ToastVisual
            {
                BindingGeneric = new ToastBindingGeneric
                {
                    Children =
                    {
                        new AdaptiveText { Text = title },
                        new AdaptiveText { Text = body }
                    }
                }
            }
        };

        var toast = new ToastNotification(toastContent.GetXml());
        
        // Show the toast
        ToastNotificationManagerCompat.CreateToastNotifier().Show(toast);
        
        // Small delay to ensure notification is sent
        await Task.Delay(100, cancellationToken);
    }
}