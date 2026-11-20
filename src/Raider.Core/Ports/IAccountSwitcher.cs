namespace Raider.Core.Ports;

/// <summary>
/// Drives Plarium Play (the launcher, outside the game process) to change the logged-in Account.
/// See docs/account-switching-findings.md: the in-game plugin cannot switch Accounts, so this happens at launcher level.
/// Implementations only select logins Plarium Play already remembers; they never read, type or store a password.
/// </summary>
public interface IAccountSwitcher
{
    /// <summary>
    /// Selects the remembered login for the specified instance in Plarium Play and launches the game with it.
    /// </summary>
    Task<AccountSwitchResult> SwitchToAsync(SwitchAccountRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to switch an account on a specific instance.
/// </summary>
public sealed record SwitchAccountRequest(
    string InstanceToken,
    string PlariumLogin
);