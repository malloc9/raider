namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Fake implementation of IGamePort for testing.
/// </summary>
public sealed class FakeGamePort : IGamePort
{
    private GameBuild _gameBuild = GameBuild.Unknown;
    private readonly Dictionary<string, Dictionary<Chore, ChoreExecutionResult>> _choreResults = new();
    private readonly Dictionary<string, string> _gameStateJson = new();
    private readonly Dictionary<string, string> _pluginLogExcerpt = new();
    private readonly Dictionary<string, Dictionary<string, AccountSwitchResult>> _switchResults = new();
    private readonly Dictionary<string, string?> _currentPlayerId = new();

    /// <summary>
    /// Sets the game build to return.
    /// </summary>
    public void SetGameBuild(GameBuild build) => _gameBuild = build;

    /// <summary>
    /// Sets the result for a specific chore on a specific instance.
    /// </summary>
    public void SetChoreResult(string instanceToken, Chore chore, ChoreExecutionResult result)
    {
        if (!_choreResults.ContainsKey(instanceToken))
            _choreResults[instanceToken] = new();
        _choreResults[instanceToken][chore] = result;
    }

    /// <summary>
    /// Sets the game state JSON to return for an instance.
    /// </summary>
    public void SetGameStateJson(string instanceToken, string json) => _gameStateJson[instanceToken] = json;

    /// <summary>
    /// Sets the plugin log excerpt to return for an instance.
    /// </summary>
    public void SetPluginLogExcerpt(string instanceToken, string excerpt) => _pluginLogExcerpt[instanceToken] = excerpt;

    /// <summary>
    /// Sets which player ID the game instance is currently logged into (null = unknown).
    /// </summary>
    public void SetCurrentPlayerId(string instanceToken, string? playerId) => _currentPlayerId[instanceToken] = playerId;

    /// <summary>
    /// Makes switching to <paramref name="playerId"/> on <paramref name="instanceToken"/> end with <paramref name="result"/> instead of succeeding.
    /// </summary>
    public void SetSwitchResult(string instanceToken, string playerId, AccountSwitchResult result)
    {
        if (!_switchResults.ContainsKey(instanceToken))
            _switchResults[instanceToken] = new(StringComparer.OrdinalIgnoreCase);
        _switchResults[instanceToken][playerId] = result;
    }

    /// <summary>
    /// Player IDs switched to, in order, per instance.
    /// </summary>
    public Dictionary<string, List<string>> SwitchedTo { get; } = new();

    /// <summary>
    /// Every call in order: "switch:<instanceToken>:<playerId>" or "chore:<instanceToken>:<playerId>:<chore id>".
    /// </summary>
    public List<string> CallLog { get; } = new();

    public Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_gameBuild);

    public Task<string?> GetCurrentPlayerIdAsync(CancellationToken cancellationToken = default)
    {
        // Return the first instance's player ID for backward compatibility
        return Task.FromResult(_currentPlayerId.Values.FirstOrDefault());
    }

    public Task<AccountSwitchResult> SwitchAccountAsync(string instanceToken, Account account, CancellationToken cancellationToken = default)
    {
        if (!SwitchedTo.ContainsKey(instanceToken))
            SwitchedTo[instanceToken] = new();
        SwitchedTo[instanceToken].Add(account.Id);
        CallLog.Add($"switch:{instanceToken}:{account.Id}");

        if (_switchResults.TryGetValue(instanceToken, out var instanceSwitches) &&
            instanceSwitches.TryGetValue(account.Id, out var result) &&
            result.Outcome != AccountSwitchOutcome.Switched)
        {
            _currentPlayerId[instanceToken] = null;
            return Task.FromResult(result);
        }

        _currentPlayerId[instanceToken] = account.Id;
        return Task.FromResult(AccountSwitchResult.Switched());
    }

    public Task<ChoreExecutionResult> ExecuteChoreAsync(string instanceToken, Chore chore, CancellationToken cancellationToken = default)
    {
        var currentPlayerId = _currentPlayerId.GetValueOrDefault(instanceToken);
        CallLog.Add($"chore:{instanceToken}:{currentPlayerId}:{chore.Id}");

        if (_choreResults.TryGetValue(instanceToken, out var instanceChores) &&
            instanceChores.TryGetValue(chore, out var result))
        {
            return Task.FromResult(result);
        }
        
        // Default to success if not configured
        return Task.FromResult(new ChoreExecutionResult(true, null, 0, null));
    }

    public Task<string> GetGameStateJsonAsync(string instanceToken, CancellationToken cancellationToken = default)
        => Task.FromResult(_gameStateJson.GetValueOrDefault(instanceToken, "{}"));

    public Task<string> GetPluginLogExcerptAsync(string instanceToken, int maxLines, CancellationToken cancellationToken = default)
        => Task.FromResult(_pluginLogExcerpt.GetValueOrDefault(instanceToken, ""));
}