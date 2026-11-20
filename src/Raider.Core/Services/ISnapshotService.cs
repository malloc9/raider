namespace Raider.Core.Services;

using Raider.Core.Models;

/// <summary>
/// Service for saving failure snapshots next to logs.
/// </summary>
public interface ISnapshotService
{
    /// <summary>
    /// Saves a failure snapshot to the log directory.
    /// </summary>
    /// <param name="logDirectory">The directory where logs are stored.</param>
    /// <param name="choreResult">The failed chore result.</param>
    /// <param name="gameStateJson">JSON representation of the game state at failure time.</param>
    /// <param name="pluginLogExcerpt">Relevant excerpt from the plugin log.</param>
    /// <returns>The path to the saved snapshot file.</returns>
    Task<string> SaveFailureSnapshotAsync(
        string logDirectory,
        ChoreResult choreResult,
        string gameStateJson,
        string pluginLogExcerpt,
        CancellationToken cancellationToken = default);
}