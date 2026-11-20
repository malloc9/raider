namespace Raider.Desktop.Services;

using System.Diagnostics;
using System.Windows.Automation;
using Microsoft.Extensions.Logging;
using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Performs an Account switch by driving Plarium Play's own "Switch account" UI through Windows UI Automation
/// (launcher-level fallback from docs/account-switching-findings.md, section 4).
/// It only clicks: picks a login Plarium Play already remembers and presses Play. If Plarium Play shows a
/// password field, it stops and reports <see cref="AccountSwitchOutcome.PasswordRequired"/>; it never types into it.
/// </summary>
/// <remarks>
/// The element names below are Plarium Play's visible labels. They are a best guess until checked against the
/// live launcher and are kept in one place so they are easy to adjust after a Plarium Play update.
/// </remarks>
public sealed class PlariumPlayAccountSwitcher : IAccountSwitcher
{
    private static readonly string[] ProcessNames = { "PlariumPlay", "Plarium Play" };
    private static readonly string[] AccountMenuNames = { "Profile", "Account", "Accounts" };
    private static readonly string[] SwitchAccountNames = { "Switch account", "Change account" };
    private static readonly string[] PlayButtonNames = { "Play", "PLAY" };

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PasswordSettleTime = TimeSpan.FromSeconds(10);

    private readonly ILogger<PlariumPlayAccountSwitcher> _logger;

    public PlariumPlayAccountSwitcher(ILogger<PlariumPlayAccountSwitcher> logger)
    {
        _logger = logger;
    }

    public Task<AccountSwitchResult> SwitchToAsync(SwitchAccountRequest request, CancellationToken cancellationToken = default)
        // UI Automation calls are blocking COM calls; keep them off the UI thread
        => Task.Run(() => SwitchTo(request, cancellationToken), cancellationToken);

    private AccountSwitchResult SwitchTo(SwitchAccountRequest request, CancellationToken cancellationToken)
    {
        var window = FindLauncherWindow();
        if (window == null)
        {
            return AccountSwitchResult.Failed("Plarium Play is not running; start it and try again");
        }

        try
        {
            // 1. Open the account menu and its "Switch account" list, unless the login is already listed
            var loginEntry = FindByName(window, request.PlariumLogin);
            if (loginEntry == null)
            {
                Invoke(WaitFor(window, AccountMenuNames, cancellationToken));
                Invoke(WaitFor(window, SwitchAccountNames, cancellationToken));
                loginEntry = WaitFor(window, new[] { request.PlariumLogin }, cancellationToken);
            }

            if (loginEntry == null)
            {
                return AccountSwitchResult.Failed($"Plarium Play does not remember the login {request.PlariumLogin}; log in to it once by hand");
            }

            // 2. Pick the remembered login
            _logger.LogInformation("Selecting remembered login {Login} for instance {InstanceToken} in Plarium Play", request.PlariumLogin, request.InstanceToken);
            Invoke(loginEntry);

            // 3. An expired remembered login makes Plarium Play ask for the password: stop there
            if (WaitForPasswordPrompt(window, cancellationToken))
            {
                _logger.LogWarning("Plarium Play asks for the password of {Login}", request.PlariumLogin);
                return AccountSwitchResult.PasswordRequired();
            }

            // 4. Launch the game for the selected Account
            var play = WaitFor(window, PlayButtonNames, cancellationToken);
            if (play == null)
            {
                return AccountSwitchResult.Failed("Could not find Plarium Play's Play button after switching");
            }

            Invoke(play);
            return AccountSwitchResult.Switched();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException)
        {
            _logger.LogError(ex, "Plarium Play automation failed");
            return AccountSwitchResult.Failed($"Plarium Play automation failed: {ex.Message}");
        }
    }

    private static AutomationElement? FindLauncherWindow()
    {
        var process = ProcessNames
            .SelectMany(Process.GetProcessesByName)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        return process == null ? null : AutomationElement.FromHandle(process.MainWindowHandle);
    }

    private static AutomationElement? FindByName(AutomationElement root, string name)
    {
        var condition = new PropertyCondition(AutomationElement.NameProperty, name, PropertyConditionFlags.IgnoreCase);
        return root.FindFirst(TreeScope.Descendants, condition);
    }

    private static AutomationElement? WaitFor(AutomationElement root, IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + StepTimeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var name in names)
            {
                var element = FindByName(root, name);
                if (element != null)
                {
                    return element;
                }
            }

            Thread.Sleep(500);
        }

        return null;
    }

    private static bool WaitForPasswordPrompt(AutomationElement root, CancellationToken cancellationToken)
    {
        var passwordField = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
            new PropertyCondition(AutomationElement.IsPasswordProperty, true));

        var deadline = DateTime.UtcNow + PasswordSettleTime;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (root.FindFirst(TreeScope.Descendants, passwordField) != null)
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return false;
    }

    private static void Invoke(AutomationElement? element)
    {
        if (element == null)
        {
            throw new InvalidOperationException("Expected Plarium Play control not found");
        }

        if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
        {
            ((InvokePattern)invoke).Invoke();
        }
        else if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select))
        {
            ((SelectionItemPattern)select).Select();
        }
        else
        {
            throw new InvalidOperationException($"Plarium Play control '{element.Current.Name}' cannot be clicked");
        }
    }
}