namespace Raider.Plugin;

using System.Net;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin.Chores;
using Raider.Plugin.Game;

/// <summary>
/// Executes chores for the desktop app's requests.
/// </summary>
internal sealed class ChoreExecutor
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, IChoreHandler> _choreHandlers;

    public ChoreExecutor()
    {
        _log = RaiderPlugin.Log;
        _choreHandlers = new Dictionary<string, IChoreHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "ClaimPlaytimeRewards", new ClaimPlaytimeRewardsChoreHandler() },
            { "ClaimQuests", new ClaimQuestsChoreHandler() },
            { "CollectInbox", new CollectInboxChoreHandler() },
            { "BuyFreeMarketItems", new BuyFreeMarketItemsChoreHandler() },
            { "CollectGemMine", new CollectGemMineChoreHandler() },
            { "ClanCheckIn", new ClanCheckInChoreHandler() }
        };
    }

    /// <summary>
    /// Handles one authenticated request that <see cref="Ipc.PluginServer"/> passed on.
    /// </summary>
    public async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            switch (request.HttpMethod)
            {
                case "GET" when request.Url?.AbsolutePath == "/raider/game-build":
                    await HandleGetGameBuildAsync(response);
                    break;
                case "GET" when request.Url?.AbsolutePath == "/raider/game-state":
                    await HandleGetGameStateAsync(response);
                    break;
                case "GET" when request.Url?.AbsolutePath?.StartsWith("/raider/plugin-log") == true:
                    await HandleGetPluginLogAsync(request, response);
                    break;
                case "GET" when request.Url?.AbsolutePath == "/raider/plugin-version":
                    await HandleGetPluginVersionAsync(response);
                    break;
                case "POST" when request.Url?.AbsolutePath == "/raider/execute-chore":
                    await HandleExecuteChoreAsync(request, response);
                    break;
                default:
                    await WriteResponseAsync(response, 404, new { error = "Not found" });
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error handling request: {ex.Message}");
            await WriteResponseAsync(response, 500, new { error = ex.Message });
        }
    }

    private async Task HandleGetGameBuildAsync(HttpListenerResponse response)
    {
        // Key is "version" to match PluginGamePort.GameBuildResponse
        await WriteResponseAsync(response, 200, new { version = GameAccess.GetGameBuild() });
    }

    private async Task HandleGetGameStateAsync(HttpListenerResponse response)
    {
        // PluginGamePort reads the response body as the raw game-state JSON
        var state = new
        {
            isInGame = GameAccess.IsInGame(),
            gameBuild = GameAccess.GetGameBuild(),
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        };
        await WriteResponseAsync(response, 200, state);
    }

    private async Task HandleGetPluginVersionAsync(HttpListenerResponse response)
    {
        await WriteResponseAsync(response, 200, new { version = PluginInfo.PLUGIN_VERSION });
    }

    private async Task HandleGetPluginLogAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var maxLines = 50;
        if (request.QueryString["maxLines"] is string maxLinesStr && int.TryParse(maxLinesStr, out var parsed))
        {
            maxLines = parsed;
        }

        // Return recent log entries (simplified for now)
        var log = $"[Plugin] Log excerpt requested ({maxLines} lines)\n[Plugin] Plugin version: {PluginInfo.PLUGIN_VERSION}\n[Plugin] Game build: {GameAccess.GetGameBuild()}\n[Plugin] In-game: {GameAccess.IsInGame()}";
        await WriteResponseAsync(response, 200, new { log });
    }

    private async Task HandleExecuteChoreAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var requestJson = await reader.ReadToEndAsync();

        var choreRequest = JsonSerializer.Deserialize<ChoreExecutionRequest>(requestJson, JsonOptions.Default);
        if (choreRequest == null || string.IsNullOrEmpty(choreRequest.ChoreType))
        {
            await WriteResponseAsync(response, 400, new { error = "Invalid request" });
            return;
        }

        _log.LogInfo($"Executing chore: {choreRequest.ChoreType}");

        if (!_choreHandlers.TryGetValue(choreRequest.ChoreType, out var handler))
        {
            await WriteResponseAsync(response, 400, new { error = $"Unknown chore type: {choreRequest.ChoreType}" });
            return;
        }

        var result = await handler.ExecuteAsync(choreRequest.Parameters);
        
        var choreResponse = new ExecuteChoreResponse
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            GemsCollected = result.Data?.GetType().GetProperty("gemsCollected")?.GetValue(result.Data) is int gems ? gems : 0,
            Data = result.Data
        };

        await WriteResponseAsync(response, 200, choreResponse);
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data, JsonOptions.Default);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }
}

/// <summary>
/// Interface for chore handlers.
/// </summary>
internal interface IChoreHandler
{
    Task<ChoreHandlerResult> ExecuteAsync(Dictionary<string, object>? parameters);
}

/// <summary>
/// Result of a chore handler execution.
/// </summary>
internal sealed record ChoreHandlerResult(
    bool Success,
    string? ErrorMessage = null,
    object? Data = null
);

/// <summary>
/// Request to execute a chore via IPC.
/// </summary>
internal sealed class ChoreExecutionRequest
{
    public string ChoreType { get; set; } = string.Empty;
    public Dictionary<string, object>? Parameters { get; set; }
}

/// <summary>
/// Response from chore execution via IPC.
/// </summary>
internal sealed class ExecuteChoreResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int GemsCollected { get; set; }
    public object? Data { get; set; }
}

/// <summary>
/// JSON serializer options.
/// </summary>
internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
