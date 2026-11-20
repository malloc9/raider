namespace Raider.Plugin.Ipc;

using System.Net;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin.Game;

/// <summary>
/// HTTP health server bound to 127.0.0.1 with token authentication.
/// Reports: in-game status, game build, plugin build.
/// </summary>
public sealed class HealthServer
{
    private readonly string _token;
    private readonly int _port;
    private readonly ManualLogSource _log;
    private readonly HttpListener _listener;
    private readonly Thread _listenerThread;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private volatile bool _isRunning;

    public HealthServer(string token, int port, ManualLogSource log)
    {
        _token = token;
        _port = port;
        _log = log;
        
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{_port}/raider/");
        
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "Raider-HealthServer"
        };
    }

    public void Start()
    {
        if (_isRunning) return;

        try
        {
            _listener.Start();
            _isRunning = true;
            _listenerThread.Start();
            _log.LogInfo($"Health server started on http://127.0.0.1:{_port}/raider/");
        }
        catch (Exception ex)
        {
            _log.LogError($"Failed to start health server: {ex.Message}");
            _isRunning = false;
        }
    }

    public void Stop()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cancellationTokenSource.Cancel();
        _listener.Stop();
        _listener.Close();
        _listenerThread.Join(TimeSpan.FromSeconds(2));
        _log.LogInfo("Health server stopped");
    }

    private void ListenLoop()
    {
        while (_isRunning && _listener.IsListening)
        {
            try
            {
                var context = _listener.GetContext();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (HttpListenerException) when (!_isRunning)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.LogError($"Error in health server listen loop: {ex.Message}");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // Require token authentication for all endpoints
            if (!ValidateToken(request))
            {
                await WriteResponseAsync(response, 401, new { error = "Invalid or missing token" });
                return;
            }

            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/raider/health")
            {
                await HandleHealthAsync(response);
            }
            else
            {
                await WriteResponseAsync(response, 404, new { error = "Not found" });
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error handling health request: {ex.Message}");
            await WriteResponseAsync(response, 500, new { error = ex.Message });
        }
    }

    private bool ValidateToken(HttpListenerRequest request)
    {
        // Allow empty token only if no token is configured (for testing)
        if (string.IsNullOrEmpty(_token))
        {
            return true;
        }

        var providedToken = request.Headers["X-Raider-Token"];
        return !string.IsNullOrEmpty(providedToken) && providedToken == _token;
    }

    private async Task HandleHealthAsync(HttpListenerResponse response)
    {
        var health = new
        {
            isInGame = GameAccess.IsInGame(),
            gameBuild = GameAccess.GetGameBuild(),
            pluginVersion = PluginInfo.PLUGIN_VERSION
        };

        await WriteResponseAsync(response, 200, health);
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