namespace Raider.Core.Services;

using System.Collections.Concurrent;
using System.Text.Json;
using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Discovers game instances by polling the instances directory.
/// Instance files are JSON files written by the in-game plugin at %LOCALAPPDATA%\Raider\instances.
/// </summary>
public sealed class InstanceRegistry : IInstanceRegistry
{
    private readonly string _instancesDirectory;
    private readonly IPluginHealthClient _healthClient;
    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _staleThreshold = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _busyTimeout = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Instance> _instances = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _busySince = new();
    private readonly ConcurrentDictionary<string, InstanceStatus> _pendingStatus = new();
    private readonly Timer _timer;
    private readonly object _refreshLock = new();
    private bool _disposed;
    private int _currentGameDay = GameDay.Current();

    public event EventHandler<InstanceRegistryChangedEventArgs> Changed = delegate { };

    public InstanceRegistry(IPluginHealthClient healthClient, string? instancesDirectory = null)
    {
        _healthClient = healthClient;
        _instancesDirectory = instancesDirectory ?? GetDefaultInstancesDirectory();
        _timer = new Timer(async _ => await PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
        
        // Ensure directory exists
        Directory.CreateDirectory(_instancesDirectory);
    }

    private static string GetDefaultInstancesDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "Raider", "instances");
    }

    /// <summary>
    /// Starts the polling timer.
    /// </summary>
    public void Start()
    {
        _timer.Change(TimeSpan.Zero, _pollInterval);
    }

    /// <summary>
    /// Stops the polling timer.
    /// </summary>
    public void Stop()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public IReadOnlyList<Instance> GetInstances()
    {
        return _instances.Values
            .OrderBy(i => i.LastSeen)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<Instance> GetReadyInstances()
    {
        return _instances.Values
            .Where(i => i.Status == InstanceStatus.Ready)
            .Where(i => i.PlayerId != null)
            .ToList()
            .AsReadOnly();
    }

    public void MarkBusy(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = InstanceStatus.Busy };
            _instances[instanceId] = updated;
            _busySince[instanceId] = DateTimeOffset.UtcNow;
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public void MarkFree(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = InstanceStatus.Ready };
            _instances[instanceId] = updated;
            _busySince.TryRemove(instanceId, out _);
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public void MarkInstanceLost(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = InstanceStatus.InstanceLost };
            _instances[instanceId] = updated;
            _busySince.TryRemove(instanceId, out _);
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public Instance? GetInstanceByPlayerId(string playerId)
    {
        return _instances.Values.FirstOrDefault(i => i.PlayerId == playerId);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await PollAsync(cancellationToken);
    }

    private async Task PollAsync(CancellationToken cancellationToken = default)
    {
        if (!Monitor.TryEnter(_refreshLock))
        {
            return; // Skip this poll if already refreshing
        }

        try
        {
            // Reset waiting instances to ready for retry
            ResetWaitingInstances();

            var newGameDay = GameDay.Current();
            if (newGameDay != _currentGameDay)
            {
                _currentGameDay = newGameDay;
                // Game day changed - reset busy timeouts so instances can run again
                foreach (var kvp in _busySince)
                {
                    _busySince[kvp.Key] = DateTimeOffset.UtcNow;
                }
            }

            var files = Directory.GetFiles(_instancesDirectory, "*.json");
            var discoveredIds = new HashSet<string>();
            var added = new List<Instance>();
            var updated = new List<Instance>();
            var removed = new List<Instance>();

            foreach (var file in files)
            {
                var instance = await ReadInstanceFileAsync(file, cancellationToken);
                if (instance == null)
                {
                    continue; // Invalid or stale file, will be cleaned up
                }

                discoveredIds.Add(instance.Id);

                if (!_instances.TryGetValue(instance.Id, out var existing))
                {
                    // New instance
                    var validated = await ValidateInstanceAsync(instance, cancellationToken);
                    _instances[instance.Id] = validated;
                    added.Add(validated);
                }
                else
                {
                    // Existing instance - update LastSeen and check health
                    var validated = await ValidateInstanceAsync(instance with { LastSeen = DateTimeOffset.UtcNow }, cancellationToken);
                    
                    // Preserve busy/duplicate status from memory
                    if (existing.Status is InstanceStatus.Busy or InstanceStatus.Duplicate)
                    {
                        validated = validated with { Status = existing.Status };
                    }
                    else if (existing.Status == InstanceStatus.Waiting)
                    {
                        // Waiting instances become Ready to be retried
                        validated = validated with { Status = InstanceStatus.Ready };
                    }
                    
                    if (!InstanceEquals(existing, validated))
                    {
                        _instances[instance.Id] = validated;
                        updated.Add(validated);
                    }
                    else
                    {
                        _instances[instance.Id] = validated; // Update LastSeen
                    }
                }
            }

            // Remove instances no longer present (and not busy)
            var toRemove = _instances.Keys.Where(id => !discoveredIds.Contains(id)).ToList();
            foreach (var id in toRemove)
            {
                if (_instances.TryRemove(id, out var removedInstance) && removedInstance.Status != InstanceStatus.Busy)
                {
                    _busySince.TryRemove(id, out _);
                    _pendingStatus.TryRemove(id, out _);
                    removed.Add(removedInstance);
                }
            }

            // Check busy timeouts
            await CheckBusyTimeoutsAsync();

            // Check for duplicates
            CheckDuplicates();

            // Clean up stale files
            CleanupStaleFiles(files);

            if (added.Count > 0 || removed.Count > 0 || updated.Count > 0)
            {
                RaiseChanged(added, removed, updated);
            }
        }
        catch (Exception)
        {
            // Log error but don't crash the polling loop
        }
        finally
        {
            Monitor.Exit(_refreshLock);
        }
    }

    private async Task<Instance?> ReadInstanceFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(filePath, cancellationToken);
            var data = JsonSerializer.Deserialize<InstanceFileData>(json);
            if (data == null) return null;

            var lastSeen = DateTimeOffset.FromUnixTimeSeconds(data.LastSeen);
            
            // Check if file is stale
            if (DateTimeOffset.UtcNow - lastSeen > _staleThreshold)
            {
                return null; // Will be cleaned up
            }

            return new Instance(
                Id: data.Id,
                Token: data.Token,
                ProcessId: data.ProcessId,
                PlayerId: data.PlayerId,
                PlayerName: data.PlayerName,
                LastSeen: lastSeen
            );
        }
        catch
        {
            return null; // Invalid JSON or missing fields
        }
    }

    private async Task<Instance> ValidateInstanceAsync(Instance instance, CancellationToken cancellationToken)
    {
        // Ignore instances without a player ID
        if (string.IsNullOrEmpty(instance.PlayerId))
        {
            return instance with { Status = InstanceStatus.Discovered };
        }

        // Check if process is still alive
        if (!IsProcessAlive(instance.ProcessId))
        {
            return instance with { Status = InstanceStatus.Dead };
        }

        // Check health endpoint
        try
        {
            var isHealthy = await _healthClient.IsHealthyAsync(instance.Token, cancellationToken);
            if (!isHealthy)
            {
                return instance with { Status = InstanceStatus.Dead };
            }
        }
        catch
        {
            return instance with { Status = InstanceStatus.Dead };
        }

        return instance with { Status = InstanceStatus.Ready };
    }

    private bool IsProcessAlive(int processId)
    {
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // Process not found
        }
        catch
        {
            return false;
        }
    }

    private async Task CheckBusyTimeoutsAsync()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kvp in _busySince)
        {
            if (!_instances.TryGetValue(kvp.Key, out var instance))
            {
                _busySince.TryRemove(kvp.Key, out _);
                continue;
            }

            if (instance.Status == InstanceStatus.Busy && now - kvp.Value > _busyTimeout)
            {
                // Mark as waiting (will be retried next pass)
                var updated = instance with { Status = InstanceStatus.Waiting };
                _instances[kvp.Key] = updated;
                RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
            }
        }
    }

    /// <summary>
    /// Resets waiting instances back to ready for retry.
    /// Called at the start of each poll cycle.
    /// </summary>
    private void ResetWaitingInstances()
    {
        var waitingInstances = _instances.Values.Where(i => i.Status == InstanceStatus.Waiting).ToList();
        foreach (var instance in waitingInstances)
        {
            var updated = instance with { Status = InstanceStatus.Ready };
            _instances[instance.Id] = updated;
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    private void CheckDuplicates()
    {
        var playerIdToInstances = _instances.Values
            .Where(i => i.PlayerId != null)
            .GroupBy(i => i.PlayerId!)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in playerIdToInstances)
        {
            var ordered = group.OrderBy(i => i.LastSeen).ToList();
            var first = ordered.First();
            
            // First instance keeps its status (or becomes Ready if it was Discovered)
            if (first.Status == InstanceStatus.Discovered)
            {
                var updated = first with { Status = InstanceStatus.Ready };
                _instances[first.Id] = updated;
            }

            // Other instances become Duplicate
            foreach (var duplicate in ordered.Skip(1))
            {
                if (duplicate.Status != InstanceStatus.Duplicate)
                {
                    var updated = duplicate with { Status = InstanceStatus.Duplicate };
                    _instances[duplicate.Id] = updated;
                }
            }
        }
    }

    private void CleanupStaleFiles(string[] currentFiles)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var file in currentFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var data = JsonSerializer.Deserialize<InstanceFileData>(json);
                if (data == null)
                {
                    File.Delete(file);
                    continue;
                }

                var lastSeen = DateTimeOffset.FromUnixTimeSeconds(data.LastSeen);
                if (now - lastSeen > _staleThreshold)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                try { File.Delete(file); } catch { }
            }
        }
    }

    private bool InstanceEquals(Instance a, Instance b)
    {
        return a.Id == b.Id &&
               a.Token == b.Token &&
               a.ProcessId == b.ProcessId &&
               a.PlayerId == b.PlayerId &&
               a.PlayerName == b.PlayerName &&
               a.Status == b.Status;
    }

    private void RaiseChanged(IReadOnlyList<Instance> added, IReadOnlyList<Instance> removed, IReadOnlyList<Instance> updated)
    {
        var args = new InstanceRegistryChangedEventArgs(added, removed, updated);
        Changed(this, args);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        await Task.CompletedTask;
    }

    private sealed class InstanceFileData
    {
        public string Id { get; set; } = "";
        public string Token { get; set; } = "";
        public int ProcessId { get; set; }
        public string? PlayerId { get; set; }
        public string? PlayerName { get; set; }
        public long LastSeen { get; set; }
    }
}