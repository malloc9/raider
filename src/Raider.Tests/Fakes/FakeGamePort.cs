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
    private readonly Dictionary<string, AccountSwitchResult> _switchResults = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentPlariumLogin;

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

    /// <summary>
    /// Sets which Plarium login the game is currently logged into (null = unknown).
    /// </summary>
    public void SetCurrentPlariumLogin(string? plariumLogin) => _currentPlariumLogin = plariumLogin;

    /// <summary>
    /// Makes switching to <paramref name="plariumLogin"/> end with <paramref name="result"/> instead of succeeding.
    /// </summary>
    public void SetSwitchResult(string plariumLogin, AccountSwitchResult result) => _switchResults[plariumLogin] = result;

    /// <summary>
    /// Plarium logins switched to, in order.
    /// </summary>
    public List<string> SwitchedTo { get; } = new();

    /// <summary>
    /// Every call in order: "switch:&lt;login&gt;" or "chore:&lt;login&gt;:&lt;chore id&gt;".
    /// </summary>
    public List<string> CallLog { get; } = new();

    public Task<string?> GetCurrentPlariumLoginAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_currentPlariumLogin);

    public Task<AccountSwitchResult> SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        SwitchedTo.Add(account.PlariumLogin);
        CallLog.Add($"switch:{account.PlariumLogin}");

        if (_switchResults.TryGetValue(account.PlariumLogin, out var result) && result.Outcome != AccountSwitchOutcome.Switched)
        {
            // Like the real launcher: after a failed switch, which Account is active is unknown
            _currentPlariumLogin = null;
            return Task.FromResult(result);
        }

        _currentPlariumLogin = account.PlariumLogin;
        return Task.FromResult(AccountSwitchResult.Switched());
    }

    public Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default)
    {
        CallLog.Add($"chore:{_currentPlariumLogin}:{chore.Id}");

        if (_choreResults.TryGetValue(chore, out var result))
        {
            return Task.FromResult(result);
        }
        
        // Default to success if not configured
        return Task.FromResult(new ChoreExecutionResult(true, null, 0, null));
    }

    public Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_gameStateJson);

    public Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default)
        => Task.FromResult(_pluginLogExcerpt);
}