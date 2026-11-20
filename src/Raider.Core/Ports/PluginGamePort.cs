namespace Raider.Core.Ports;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Raider.Core.Models;

/// <summary>
/// Game port implementation that communicates with the in-process plugin via HTTP.
/// </summary>
public sealed class PluginGamePort : IGamePort
{
    private readonly HttpClient _httpClient;
    private readonly string _pluginBaseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IAccountSwitcher? _accountSwitcher;
    private readonly TimeSpan _pluginStartupTimeout = TimeSpan.FromMinutes(3);
    private string? _currentPlariumLogin;

    public PluginGamePort(IAccountSwitcher? accountSwitcher = null, string pluginBaseUrl = "http://localhost:51838/raider/")
    {
        _accountSwitcher = accountSwitcher;
        _pluginBaseUrl = pluginBaseUrl.TrimEnd('/') + "/";
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(_pluginBaseUrl),
            Timeout = TimeSpan.FromSeconds(60)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public async Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("game-build", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GameBuildResponse>(_jsonOptions, cancellationToken);
                if (result != null && !string.IsNullOrEmpty(result.Version))
                {
                    return new GameBuild(result.Version, DateTimeOffset.UtcNow);
                }
            }
        }
        catch (Exception)
        {
            // Log error but don't throw - return unknown
        }
        
        return GameBuild.Unknown;
    }

    /// <inheritdoc />
    public Task<string?> GetCurrentPlariumLoginAsync(CancellationToken cancellationToken = default)
    {
        // The game process cannot tell which Plarium login it runs under (see docs/account-switching-findings.md),
        // so this is the last login Raider switched to. Null until the first switch, which forces one.
        return Task.FromResult(_currentPlariumLogin);
    }

    /// <inheritdoc />
    public async Task<AccountSwitchResult> SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        if (_accountSwitcher == null)
        {
            return AccountSwitchResult.Failed("Account switching is not available");
        }

        // The relaunched game must not be confused with the old one still answering
        _currentPlariumLogin = null;

        var result = await _accountSwitcher.SwitchToAsync(account.PlariumLogin, cancellationToken);
        if (result.Outcome != AccountSwitchOutcome.Switched)
        {
            return result;
        }

        // Plarium Play relaunches the game; Chores need the plugin back online.
        var deadline = DateTimeOffset.UtcNow + _pluginStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await IsPluginHealthyAsync(cancellationToken))
            {
                _currentPlariumLogin = account.PlariumLogin;
                return result;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        return AccountSwitchResult.Failed($"Game did not come back within {_pluginStartupTimeout.TotalMinutes:0} minutes after the Account switch");
    }

    /// <inheritdoc />
    public async Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default)
    {
        var request = new PluginChoreRequest
        {
            ChoreType = chore.Type.ToString(),
            Parameters = new Dictionary<string, object>()
        };

        try
        {
            var response = await _httpClient.PostAsJsonAsync("execute-chore", request, _jsonOptions, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                return new ChoreExecutionResult(false, $"Plugin returned {response.StatusCode}: {errorContent}");
            }

            var result = await response.Content.ReadFromJsonAsync<PluginChoreResponse>(_jsonOptions, cancellationToken);
            
            if (result == null)
            {
                return new ChoreExecutionResult(false, "Plugin returned null response");
            }

            // Extract gems collected from Data if present
            int gemsCollected = 0;
            if (result.Data is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("gemsCollected", out var gemsProp) && gemsProp.ValueKind == JsonValueKind.Number)
                {
                    gemsCollected = gemsProp.GetInt32();
                }
            }

            return new ChoreExecutionResult(result.Success, result.ErrorMessage, gemsCollected, result.Data);
        }
        catch (HttpRequestException ex)
        {
            return new ChoreExecutionResult(false, $"Failed to communicate with plugin: {ex.Message}");
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return new ChoreExecutionResult(false, "Plugin request timed out");
        }
        catch (Exception ex)
        {
            return new ChoreExecutionResult(false, $"Unexpected error: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("game-state", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore errors
        }
        return "{}";
    }

    /// <inheritdoc />
    public async Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"plugin-log?maxLines={maxLines}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PluginLogResponse>(_jsonOptions, cancellationToken);
                return result?.Log ?? "";
            }
        }
        catch
        {
            // Ignore errors
        }
        return "";
    }

    /// <summary>
    /// Checks if the plugin is reachable.
    /// </summary>
    public async Task<bool> IsPluginHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the plugin version.
    /// </summary>
    public async Task<string> GetPluginVersionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("plugin-version", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<PluginVersionResponse>(_jsonOptions, cancellationToken);
                return result?.Version ?? "unknown";
            }
        }
        catch
        {
            // Ignore errors
        }
        return "unknown";
    }

    /// <summary>
    /// Request sent to the plugin to execute a chore.
    /// </summary>
    private sealed class PluginChoreRequest
    {
        public string ChoreType { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    /// <summary>
    /// Response received from the plugin after chore execution.
    /// </summary>
    private sealed class PluginChoreResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public object? Data { get; set; }
    }

    private sealed class GameBuildResponse
    {
        public string Version { get; set; } = string.Empty;
    }

    private sealed class PluginLogResponse
    {
        public string Log { get; set; } = string.Empty;
    }

    private sealed class PluginVersionResponse
    {
        public string Version { get; set; } = string.Empty;
    }
}