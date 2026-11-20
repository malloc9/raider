namespace Raider.Plugin;

using System.Net;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin.Chores;

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
            { "ClaimPlaytimeRewards", new ClaimPlaytimeRewardsChoreHandler() },
            { "ClaimQuests", new ClaimQuestsChoreHandler() },
            { "CollectInbox", new CollectInboxChoreHandler() },
            { "BuyFreeMarketItems", new BuyFreeMarketItemsChoreHandler() },
            { "CollectGemMine", new CollectGemMineChoreHandler() },
            { "ClanCheckIn", new ClanCheckInChoreHandler() }
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
        _httpListener.Prefixes.Add("http://localhost:51837/raider/");
        
        try
        {
            _httpListener.Start();
            _isRunning = true;
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
            _listenerThread.Start();
            _log.LogInfo("IPC server started on http://localhost:51837/raider/");
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
                // Expected when stopping
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
            if (request.HttpMethod == "POST" && request.Url?.AbsolutePath == "/raider/execute-chore")
            {
                await HandleExecuteChoreAsync(request, response);
            }
            else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/health")
            {
                await WriteResponseAsync(response, 200, new { status = "ok" });
            }
            else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/game-build")
            {
                await HandleGetGameBuildAsync(response);
            }
            else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/game-state")
            {
                await HandleGetGameStateAsync(response);
            }
            else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/plugin-log")
            {
                await HandleGetPluginLogAsync(request, response);
            }
            else if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/plugin-version")
            {
                await HandleGetPluginVersionAsync(response);
            }
            else
            {
                await WriteResponseAsync(response, 404, new { error = "Not found" });
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error handling request: {ex.Message}");
            await WriteResponseAsync(response, 500, new { error = ex.Message });
        }
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
        
        var choreResponse = new ChoreExecutionResponse
        {
            Success = result.Success,
            ErrorMessage = result.ErrorMessage,
            Data = result.Data
        };

        await WriteResponseAsync(response, 200, choreResponse);
    }

    private async Task HandleGetGameBuildAsync(HttpListenerResponse response)
    {
        var gameBuild = GetGameBuildFromGame();
        await WriteResponseAsync(response, 200, new { version = gameBuild });
    }

    private async Task HandleGetGameStateAsync(HttpListenerResponse response)
    {
        var gameState = await GetGameStateJsonAsync();
        await WriteResponseAsync(response, 200, gameState);
    }

    private async Task HandleGetPluginLogAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var maxLines = 50;
        if (request.QueryString["maxLines"] != null && int.TryParse(request.QueryString["maxLines"], out var parsed))
        {
            maxLines = parsed;
        }
        
        var logExcerpt = GetPluginLogExcerpt(maxLines);
        await WriteResponseAsync(response, 200, new { log = logExcerpt });
    }

    private async Task HandleGetPluginVersionAsync(HttpListenerResponse response)
    {
        await WriteResponseAsync(response, 200, new { version = PluginInfo.PLUGIN_VERSION });
    }

    private string GetGameBuildFromGame()
    {
        try
        {
            var gameAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            
            if (gameAssembly != null)
            {
                var version = gameAssembly.GetName().Version;
                if (version != null)
                {
                    return version.ToString();
                }
            }
            
            var unityEngineAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "UnityEngine.CoreModule");
            if (unityEngineAssembly != null)
            {
                var appType = unityEngineAssembly.GetType("UnityEngine.Application");
                if (appType != null)
                {
                    var versionProp = appType.GetProperty("version", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (versionProp != null)
                    {
                        var version = versionProp.GetValue(null);
                        if (version != null)
                        {
                            return version.ToString()!;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting game build: {ex.Message}");
        }
        
        return "unknown";
    }

    private async Task<string> GetGameStateJsonAsync()
    {
        await Task.Yield();
        
        try
        {
            return "{\"inGame\":true,\"scene\":\"Town\"}";
        }
        catch (Exception ex)
        {
            _log.LogError($"Error getting game state: {ex.Message}");
            return "{}";
        }
    }

    private string GetPluginLogExcerpt(int maxLines)
    {
        return "[Plugin] Log excerpt requested";
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
internal sealed class ChoreExecutionResponse
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
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