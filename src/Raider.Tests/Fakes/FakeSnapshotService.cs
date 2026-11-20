namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Services;

/// <summary>
/// Fake implementation of ISnapshotService for testing.
/// </summary>
public sealed class FakeSnapshotService : ISnapshotService
{
    public string LastSnapshotPath { get; private set; } = "";
    public int SnapshotCount { get; private set; }
    public List<(ChoreResult Result, string GameStateJson, string PluginLogExcerpt)> SavedSnapshots { get; } = new();

    public Task<string> SaveFailureSnapshotAsync(
        string logDirectory,
        ChoreResult choreResult,
        string gameStateJson,
        string pluginLogExcerpt,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(logDirectory, $"snapshot-{SnapshotCount}.json");
        LastSnapshotPath = path;
        SnapshotCount++;
        SavedSnapshots.Add((choreResult, gameStateJson, pluginLogExcerpt));
        return Task.FromResult(path);
    }

    public void Reset()
    {
        LastSnapshotPath = "";
        SnapshotCount = 0;
        SavedSnapshots.Clear();
    }
}