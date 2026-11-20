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
        var body = run.Status == RunStatus.SwitchFailed
            ? $"Account {run.Account.DisplayName}: account switch failed: {run.StatusMessage}"
            : $"Account {run.Account.DisplayName}: {run.FailureCount} chore(s) failed";

        if (run.IsGameBuildMismatch)
        {
            body += $" ({run.BrokenByGameUpdateCount} broken by game update)";
        }

        await ShowToastAsync(title, body, cancellationToken);
    }

    public async Task ShowManualLoginRequiredNotificationAsync(Account account, CancellationToken cancellationToken = default)
    {
        await ShowToastAsync(
            "Raider: log in by hand",
            $"Account {account.DisplayName} ({account.PlariumLogin}) needs its password. Log in to it in Plarium Play, then run it again.",
            cancellationToken);
    }

    private static async Task ShowToastAsync(string title, string body, CancellationToken cancellationToken)
    {
        new ToastContentBuilder()
            .AddText(title)
            .AddText(body)
            .Show();

        // Small delay to ensure notification is sent
        await Task.Delay(100, cancellationToken);
    }
}