using Raider.Core.Models;
using Raider.Core.Ports;
using System;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Raider.Desktop.Services;

/// <summary>
/// Implementation of IGamePort that communicates with the in-game plugin via named pipes.
/// </summary>
public sealed class GamePort : IGamePort
{
    private const string PipeName = "raider-plugin-ipc";
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _operationTimeout = TimeSpan.FromSeconds(30);

    public async Task<GameBuild> GetGameBuildAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(new IpcRequest("GetGameBuild"), cancellationToken);
        if (response.Success && !string.IsNullOrEmpty(response.Data))
        {
            return new GameBuild(response.Data, DateTimeOffset.UtcNow);
        }
        
        // Fallback
        return GameBuild.Unknown;
    }

    public async Task SwitchAccountAsync(Account account, CancellationToken cancellationToken = default)
    {
        // TODO: Implement account switching via plugin
        // For now, just log
        await Task.CompletedTask;
    }

    public async Task<ChoreExecutionResult> ExecuteChoreAsync(Chore chore, CancellationToken cancellationToken = default)
    {
        var request = new IpcRequest("ExecuteChore", chore.Type.ToString());
        var response = await SendRequestAsync(request, cancellationToken);
        
        return new ChoreExecutionResult(
            response.Success,
            response.ErrorMessage,
            response.GemsCollected);
    }

    public async Task<string> GetGameStateJsonAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(new IpcRequest("GetGameState"), cancellationToken);
        return response.Success && !string.IsNullOrEmpty(response.Data) 
            ? response.Data 
            : "{}";
    }

    public async Task<string> GetPluginLogExcerptAsync(int maxLines, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync(new IpcRequest("GetPluginLog"), cancellationToken);
        return response.Success && !string.IsNullOrEmpty(response.Data) 
            ? response.Data 
            : "";
    }

    private async Task<IpcResponse> SendRequestAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_operationTimeout);

        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            
            // Connect with timeout
            var connectTask = client.ConnectAsync((int)_connectionTimeout.TotalMilliseconds, cts.Token);
            var timeoutTask = Task.Delay(_connectionTimeout, cts.Token);
            
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask == timeoutTask)
            {
                return new IpcResponse(false, "Connection timeout");
            }
            
            await connectTask; // Propagate any exception

            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(client, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            // Send request
            var requestJson = JsonSerializer.Serialize(request);
            await writer.WriteLineAsync(requestJson);

            // Read response
            var responseJson = await reader.ReadLineAsync();
            if (string.IsNullOrEmpty(responseJson))
            {
                return new IpcResponse(false, "Empty response");
            }

            var response = JsonSerializer.Deserialize<IpcResponse>(responseJson);
            return response ?? new IpcResponse(false, "Invalid response format");
        }
        catch (OperationCanceledException)
        {
            return new IpcResponse(false, "Operation timeout");
        }
        catch (Exception ex)
        {
            return new IpcResponse(false, $"IPC error: {ex.Message}");
        }
    }
}

/// <summary>
/// IPC request to plugin.
/// </summary>
internal record IpcRequest(
    string Command,
    string? ChoreName = null
);

/// <summary>
/// IPC response from plugin.
/// </summary>
internal record IpcResponse(
    bool Success,
    string? ErrorMessage,
    int GemsCollected = 0,
    string? Data = null
);