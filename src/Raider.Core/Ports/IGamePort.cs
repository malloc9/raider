namespace Raider.Core.Ports;

using Raider.Core.Models;

/// <summary>
/// Port for interacting with a specific game instance.
/// Each instance has its own token for communication.
/// </summary>
public interface IGamePort
{
    /// <summary>
    /// Gets the current game build version.
    /// </summary>
    Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the player ID the running game is logged into, or null when unknown.
    /// </summary>
    Task<string?> GetCurrentPlayerIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an Account switch to the specified Account using the instance's token.
    /// Never asks for or handles a password: if one is needed, returns <see cref="AccountSwitchOutcome.PasswordRequired"/>.
    /// </summary>
    Task<AccountSwitchResult> SwitchAccountAsync(string instanceToken, Account account, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Executes a chore on the instance.
    /// </summary>
    Task<ChoreExecutionResult> ExecuteChoreAsync(string instanceToken, Chore chore, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets the current game state as JSON.
    /// </summary>
    Task<string> GetGameStateJsonAsync(string instanceToken, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets recent plugin log excerpt.
    /// </summary>
    Task<string> GetPluginLogExcerptAsync(string instanceToken, int maxLines, CancellationToken cancellationToken = default);
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