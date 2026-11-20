namespace Raider.Core.Ports;

using Raider.Core.Models;

/// <summary>
/// Port for interacting with the game process.
/// </summary>
public interface IGamePort
{
    /// <summary>
    /// Gets the current game build version.
    /// </summary>
    Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the Plarium login the running game is logged into, or null when unknown.
    /// </summary>
    Task<string?> GetCurrentPlariumLoginAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an Account switch to the specified Account, using logins Plarium Play already remembers.
    /// Never asks for or handles a password: if one is needed, returns <see cref="AccountSwitchOutcome.PasswordRequired"/>.
    /// </summary>
    Task<AccountSwitchResult> SwitchAccountAsync(Account account, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a chore.
    /// </summary>
    Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current game state as JSON.
    /// </summary>
    Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets recent plugin log excerpt.
    /// </summary>
    Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of executing a chore through the game port.
/// </summary>
public sealed record ChoreExecutionResult(
    bool Success,
    string? ErrorMessage = null,
    int GemsCollected = 0,
    object? Data = null
);