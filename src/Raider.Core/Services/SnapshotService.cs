namespace Raider.Core.Services;

using Raider.Core.Models;
using System.Text.Json;

/// <summary>
/// Implementation of ISnapshotService that saves failure snapshots as JSON files next to logs.
/// </summary>
public sealed class SnapshotService : ISnapshotService
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<string> SaveFailureSnapshotAsync(
        string logDirectory,
        ChoreResult choreResult,
        string gameStateJson,
        string pluginLogExcerpt,
        CancellationToken cancellationToken = default)
    {
        var snapshot = new FailureSnapshot(
            Id: Guid.NewGuid().ToString("N")[..12],
            ChoreId: choreResult.Chore.Id,
            ChoreName: choreResult.Chore.Name,
            Outcome: choreResult.Outcome,
            ErrorMessage: choreResult.ErrorMessage ?? "Unknown error",
            GameStateJson: gameStateJson,
            PluginLogExcerpt: pluginLogExcerpt,
            CapturedAt: DateTimeOffset.UtcNow);

        var fileName = snapshot.FileName;
        var filePath = Path.Combine(logDirectory, fileName);

        // Ensure directory exists
        Directory.CreateDirectory(logDirectory);

        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        return filePath;
    }
}