namespace Raider.Plugin.Ipc;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BepInEx.Logging;
using Raider.Plugin.Game;

/// <summary>
/// The plugin's one HTTP server, on a free localhost port, with token authentication on every request.
/// Serves /raider/health itself and hands every other path to the <see cref="ChoreExecutor"/>.
/// Several Raid processes can run the plugin from one install; each gets its own port.
/// </summary>
public sealed class PluginServer
{
    private const int MaxBindAttempts = 10;

    private readonly string _token;
    private readonly ManualLogSource _log;
    private readonly Func<HttpListenerContext, Task> _handleOtherRequest;
    private readonly Thread _listenerThread;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private HttpListener _listener = new();
    private volatile bool _isRunning;

    public PluginServer(string token, ManualLogSource log, Func<HttpListenerContext, Task> handleOtherRequest)
    {
        _token = token;
        _log = log;
        _handleOtherRequest = handleOtherRequest;

        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "Raider-PluginServer"
        };
    }

    /// <summary>The port the server listens on, or 0 when it is not running.</summary>
    public int Port { get; private set; }

    public void Start()
    {
        if (_isRunning) return;

        for (var attempt = 1; attempt <= MaxBindAttempts && !_isRunning; attempt++)
        {
            var port = FindFreePort();
            var listener = new HttpListener();
            // "localhost" (not 127.0.0.1) needs no URL reservation, so a non-elevated game can bind it
            listener.Prefixes.Add($"http://localhost:{port}/raider/");

            try
            {
                listener.Start();
                _listener = listener;
                Port = port;
                _isRunning = true;
            }
            catch (HttpListenerException ex)
            {
                // Another process took the port between probing and binding; try another one
                _log.LogWarning($"Could not bind port {port} (attempt {attempt}): {ex.Message}");
                listener.Close();
            }
        }

        if (!_isRunning)
        {
            _log.LogError($"Failed to start plugin server after {MaxBindAttempts} attempts");
            return;
        }

        _listenerThread.Start();
        _log.LogInfo($"Plugin server started on http://localhost:{Port}/raider/");
    }

    private static int FindFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try
        {
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }
        finally
        {
            probe.Stop();
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
        Port = 0;
        _log.LogInfo("Plugin server stopped");
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
                _log.LogError($"Error in plugin server listen loop: {ex.Message}");
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
                await _handleOtherRequest(context);
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error handling request: {ex.Message}");
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