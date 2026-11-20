namespace Raider.Core.Ports;

using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Raider.Core.Models;

/// <summary>
/// HTTP client for the plugin's health endpoint, through a <see cref="PluginConnection"/>.
/// </summary>
public sealed class PluginHealthClient : IPluginHealthClient
{
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions;

    public PluginHealthClient(PluginConnection connection, HttpMessageHandler? innerHandler = null)
    {
        _httpClient = new HttpClient(connection.CreateHandler(innerHandler))
        {
            BaseAddress = PluginConnection.PlaceholderBaseUrl,
            Timeout = TimeSpan.FromSeconds(5)
        };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <inheritdoc />
    public async Task<PluginHealth?> GetHealthAsync(string token, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "health");
            request.Headers.Add("X-Raider-Token", token);
            
            var response = await _httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var healthResponse = await response.Content.ReadFromJsonAsync<HealthResponse>(_jsonOptions, cancellationToken);
            
            if (healthResponse == null)
            {
                return null;
            }

            return new PluginHealth(
                IsInGame: healthResponse.IsInGame,
                GameBuild: new GameBuild(healthResponse.GameBuild, DateTimeOffset.UtcNow),
                PluginVersion: healthResponse.PluginVersion,
                Timestamp: DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(string token, CancellationToken cancellationToken = default)
    {
        var health = await GetHealthAsync(token, cancellationToken);
        return health != null;
    }

    /// <summary>
    /// Response from the plugin's health endpoint.
    /// </summary>
    private sealed class HealthResponse
    {
        public bool IsInGame { get; set; }
        public string GameBuild { get; set; } = string.Empty;
        public string PluginVersion { get; set; } = string.Empty;
    }
}