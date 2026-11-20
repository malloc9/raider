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
    /// Switches to the specified account.
    /// </summary>
    Task SwitchAccountAsync(Account account, CancellationToken cancellationToken = default);
    
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
    string? ErrorMessage = null
);