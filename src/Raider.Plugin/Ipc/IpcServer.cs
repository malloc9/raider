using BepInEx.Logging;
using Raider.Plugin.Chores;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Raider.Plugin.Ipc;

/// <summary>
/// Named pipe server for communication with the Desktop app.
/// </summary>
public static class IpcServer
{
    private static ManualLogSource? _logger;
    private static readonly ConcurrentDictionary<string, Func<ChoreResult>> _choreHandlers = new();
    private static Thread? _serverThread;
    private static CancellationTokenSource? _cancellationTokenSource;
    private const string PipeName = "raider-plugin-ipc";

    public static void RegisterChoreHandler(string choreName, Func<ChoreResult> handler)
    {
        _choreHandlers[choreName] = handler;
        _logger?.LogDebug($"Registered chore handler: {choreName}");
    }

    public static void Start(ManualLogSource logger)
    {
        _logger = logger;
        _cancellationTokenSource = new CancellationTokenSource();
        
        _serverThread = new Thread(() => RunServer(_cancellationTokenSource.Token))
        {
            IsBackground = true,
            Name = "Raider-IpcServer"
        };
        _serverThread.Start();
        
        _logger.LogInfo("IPC server started");
    }

    public static void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _serverThread?.Join(TimeSpan.FromSeconds(2));
        _logger?.LogInfo("IPC server stopped");
    }

    private static void RunServer(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous);

                _logger?.LogDebug("Waiting for client connection...");
                server.WaitForConnection(cancellationToken);
                
                if (cancellationToken.IsCancellationRequested) break;

                _logger?.LogDebug("Client connected");
                HandleClient(server, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError($"IPC server error: {ex}");
                Thread.Sleep(1000); // Back off on error
            }
        }
    }

    private static void HandleClient(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            while (server.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                var requestJson = reader.ReadLine();
                if (requestJson == null) break;

                _logger?.LogDebug($"Received request: {requestJson}");

                var request = JsonSerializer.Deserialize<IpcRequest>(requestJson);
                if (request == null)
                {
                    SendResponse(writer, new IpcResponse(false, "Invalid request"));
                    continue;
                }

                var response = ProcessRequest(request);
                var responseJson = JsonSerializer.Serialize(response);
                writer.WriteLine(responseJson);
                
                _logger?.LogDebug($"Sent response: {responseJson}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error handling client: {ex}");
        }
        finally
        {
            server.Disconnect();
        }
    }

    private static IpcResponse ProcessRequest(IpcRequest request)
    {
        try
        {
            switch (request.Command)
            {
                case "ExecuteChore":
                    if (_choreHandlers.TryGetValue(request.ChoreName, out var handler))
                    {
                        var result = handler();
                        return new IpcResponse(true, null, result.GemsCollected);
                    }
                    return new IpcResponse(false, $"Unknown chore: {request.ChoreName}");

                case "GetGameBuild":
                    // Return a dummy build version for now
                    return new IpcResponse(true, null, 0, "1.0.0");

                case "GetGameState":
                    return new IpcResponse(true, null, 0, "{}");

                case "GetPluginLog":
                    return new IpcResponse(true, null, 0, "Plugin log excerpt");

                default:
                    return new IpcResponse(false, $"Unknown command: {request.Command}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError($"Error processing request: {ex}");
            return new IpcResponse(false, ex.Message);
        }
    }

    private static void SendResponse(StreamWriter writer, IpcResponse response)
    {
        var json = JsonSerializer.Serialize(response);
        writer.WriteLine(json);
    }
}

/// <summary>
/// IPC request from Desktop app to plugin.
/// </summary>
internal record IpcRequest(
    string Command,
    string? ChoreName = null
);

/// <summary>
/// IPC response from plugin to Desktop app.
/// </summary>
internal record IpcResponse(
    bool Success,
    string? ErrorMessage,
    int GemsCollected = 0,
    string? Data = null
);