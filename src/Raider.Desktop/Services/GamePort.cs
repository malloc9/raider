namespace Raider.Desktop.Services;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Implementation of IGamePort that communicates with the in-game plugin via HTTP.
/// </summary>
public sealed class GamePort : IGamePort
{
    private const string PluginBaseUrl = "http://localhost:51838/raider/";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public GamePort()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(PluginBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("game-build", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GameBuildResponse>(_jsonOptions, cancellationToken);
                if (result != null && !string.IsNullOrEmpty(result.GameBuild))
                {
                    return new GameBuild(result.GameBuild, DateTimeOffset.UtcNow);
                }
            }
        }
        catch
        {
            // Ignore errors, return unknown
        }
        
        return GameBuild.Unknown;
    }

    public async Task SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new SwitchAccountRequest { PlariumLogin = account.PlariumLogin };
            var response = await _httpClient.PostAsJsonAsync("switch-account", request, _jsonOptions, cancellationToken);
            // Fire and forget for now
        }
        catch
        {
            // Ignore errors for now
        }
        await Task.CompletedTask;
    }

    public async Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default)
    {
        var request = new ExecuteChoreRequest
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

            var result = await response.Content.ReadFromJsonAsync<ExecuteChoreResponse>(_jsonOptions, cancellationToken);
            
            if (result == null)
            {
                return new ChoreExecutionResult(false, "Plugin returned null response");
            }

            return new ChoreExecutionResult(result.Success, result.ErrorMessage, result.GemsCollected, result.Data);
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

    public async Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("game-state", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<GameStateResponse>(_jsonOptions, cancellationToken);
                return result?.State ?? "{}";
            }
        }
        catch
        {
            // Ignore errors
        }
        return "{}";
    }

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

    private sealed class GameBuildResponse
    {
        public string GameBuild { get; set; } = string.Empty;
    }

    private sealed class SwitchAccountRequest
    {
        public string PlariumLogin { get; set; } = string.Empty;
    }

    private sealed class ExecuteChoreRequest
    {
        public string ChoreType { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    private sealed class ExecuteChoreResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public int GemsCollected { get; set; }
        public object? Data { get; set; }
    }

    private sealed class GameStateResponse
    {
        public string State { get; set; } = string.Empty;
    }

    private sealed class PluginLogResponse
    {
        public string Log { get; set; } = string.Empty;
    }
}