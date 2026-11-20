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

    public PluginGamePort(string pluginBaseUrl = "http://localhost:51837/raider/")
    {
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
        // The plugin doesn't expose game build directly; we'd get this from the game process
        // For now, return unknown - the desktop app can get this separately
        return await Task.FromResult(GameBuild.Unknown);
    }

    /// <inheritdoc />
    public async Task SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        // Account switching is handled by the desktop app via Plarium Play automation
        // The plugin just needs to be aware of the current account
        await Task.CompletedTask;
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

            return new ChoreExecutionResult(result.Success, result.ErrorMessage, result.Data);
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
        // Could be implemented via a plugin endpoint
        return await Task.FromResult("{}");
    }

    /// <inheritdoc />
    public async Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        // Could be implemented via a plugin endpoint
        return await Task.FromResult($"[Plugin] Log excerpt requested ({maxLines} lines)");
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
}