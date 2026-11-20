namespace Raider.Plugin;

using System.Net;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin.Chores;
using Raider.Plugin.Game;

/// <summary>
/// Executes chores and handles IPC communication with the desktop app.
/// </summary>
internal sealed class ChoreExecutor
{
    private readonly ManualLogSource _log;
    private readonly Dictionary<string, IChoreHandler> _choreHandlers;
    private HttpListener? _httpListener;
    private Thread? _listenerThread;
    private bool _isRunning;

    public ChoreExecutor()
    {
        _log = RaiderPlugin.Log;
        _choreHandlers = new Dictionary<string, IChoreHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "ClanCheckIn", new ClanCheckInChoreHandler() },
            { "CollectInbox", new CollectInboxChoreHandler() },
            { "BuyFreeMarketItems", new BuyFreeMarketItemsChoreHandler() },
            { "CollectGemMine", new CollectGemMineChoreHandler() },
            { "ClaimPlaytimeRewards", new ClaimPlaytimeRewardsChoreHandler() },
            { "ClaimQuests", new ClaimQuestsChoreHandler() },
        };
    }

    /// <summary>
    /// Starts the HTTP server for IPC communication.
    /// </summary>
    public async Task StartIpcServerAsync()
    {
        if (_isRunning)
            return;

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add("http://localhost:51838/raider/");
        
        try
        {
            _httpListener.Start();
            _isRunning = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();
            _log.LogInfo("IPC server started on http://localhost:51838/raider/");
        }
        catch (Exception ex)
        {
            _log.LogError($"Failed to start IPC server: {ex.Message}");
            _isRunning = false;
        }
    }

    /// <summary>
    /// Stops the HTTP server.
    /// </summary>
    public void StopIpcServer()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _httpListener?.Stop();
        _httpListener?.Close();
        _listenerThread?.Join(TimeSpan.FromSeconds(2));
        _log.LogInfo("IPC server stopped");
    }

    private void ListenLoop()
    {
        while (_isRunning && _httpListener?.IsListening == true)
        {
            try
            {
                var context = _httpListener.GetContext();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError($"Error in IPC listen loop: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
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
                case "POST" when request.Url?.AbsolutePath == "/raider/execute-chore":
                    await HandleExecuteChoreAsync(request, response);
                    break;
                case "POST" when request.Url?.AbsolutePath == "/raider/switch-account":
                    await HandleSwitchAccountAsync(request, response);
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
        var gameBuild = GameAccess.GetGameBuild();
        await WriteResponseAsync(response, 200, new { gameBuild });
    }

    private async Task HandleGetGameStateAsync(HttpListenerResponse response)
    {
        // Return basic game state as JSON
        var state = new
        {
            isInGame = GameAccess.IsInGame(),
            gameBuild = GameAccess.GetGameBuild(),
            timestamp = DateTimeOffset.UtcNow.ToString("O")
        };
        await WriteResponseAsync(response, 200, new { state = JsonSerializer.Serialize(state, JsonOptions.Default) });
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
            GemsCollected = result.Data is int gems ? gems : 0,
            Data = result.Data
        };

        await WriteResponseAsync(response, 200, choreResponse);
    }

    private async Task HandleSwitchAccountAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        var requestJson = await reader.ReadToEndAsync();

        var switchRequest = JsonSerializer.Deserialize<SwitchAccountRequest>(requestJson, JsonOptions.Default);
        if (switchRequest == null || string.IsNullOrEmpty(switchRequest.PlariumLogin))
        {
            await WriteResponseAsync(response, 400, new { error = "Invalid request" });
            return;
        }

        _log.LogInfo($"Switch account requested: {switchRequest.PlariumLogin}");
        // Account switching is handled by the desktop app via Plarium Play automation
        // The plugin just acknowledges the request
        await WriteResponseAsync(response, 200, new { success = true, message = "Account switch acknowledged" });
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
/// Request to switch account.
/// </summary>
internal sealed class SwitchAccountRequest
{
    public string PlariumLogin { get; set; } = string.Empty;
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