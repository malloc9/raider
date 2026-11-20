namespace Raider.Core.Models;

/// <summary>
/// How an Account switch ended.
/// </summary>
public enum AccountSwitchOutcome
{
    /// <summary>The game is now logged into the requested Account.</summary>
    Switched,

    /// <summary>Plarium Play's remembered login has expired and it asks for the password; the player must log in by hand.</summary>
    PasswordRequired,

    /// <summary>The switch failed for another reason.</summary>
    Failed
}

/// <summary>
/// Result of an Account switch. Never carries credentials.
/// </summary>
public sealed record AccountSwitchResult(AccountSwitchOutcome Outcome, string? ErrorMessage = null)
{
    public static AccountSwitchResult Switched() => new(AccountSwitchOutcome.Switched);

    public static AccountSwitchResult PasswordRequired() =>
        new(AccountSwitchOutcome.PasswordRequired, "Plarium Play asks for the password; log in by hand");

    public static AccountSwitchResult Failed(string errorMessage) => new(AccountSwitchOutcome.Failed, errorMessage);
}
