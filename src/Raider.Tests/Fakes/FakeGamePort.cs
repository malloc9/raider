namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Fake implementation of IGamePort for testing.
/// </summary>
public sealed class FakeGamePort : IGamePort
{
    private GameBuild _gameBuild = GameBuild.Unknown;
    private readonly Dictionary<Chore, ChoreExecutionResult> _choreResults = new();
    private string _gameStateJson = "{}";
    private string _pluginLogExcerpt = "";
    private Account? _currentAccount;

    /// <summary>
    /// Sets the game build to return.
    /// </summary>
    public void SetGameBuild(GameBuild build) => _gameBuild = build;

    /// <summary>
    /// Sets the result for a specific chore.
    /// </summary>
    public void SetChoreResult(Chore chore, ChoreExecutionResult result) => _choreResults[chore] = result;

    /// <summary>
    /// Sets the game state JSON to return.
    /// </summary>
    public void SetGameStateJson(string json) => _gameStateJson = json;

    /// <summary>
    /// Sets the plugin log excerpt to return.
    /// </summary>
    public void SetPluginLogExcerpt(string excerpt) => _pluginLogExcerpt = excerpt;

    public Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_gameBuild);

    public Task SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        _currentAccount = account;
        return Task.CompletedTask;
    }

    public Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default)
    {
        if (_choreResults.TryGetValue(chore, out var result))
        {
            return Task.FromResult(result);
        }
        
        // Default to success if not configured
        return Task.FromResult(new ChoreExecutionResult(true));
    }

    public Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_gameStateJson);

    public Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default)
        => Task.FromResult(_pluginLogExcerpt);
}