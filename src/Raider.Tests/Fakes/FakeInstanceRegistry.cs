namespace Raider.Tests.Fakes;

using Raider.Core.Models;
using Raider.Core.Ports;

/// <summary>
/// Fake implementation of IInstanceRegistry for testing.
/// </summary>
public sealed class FakeInstanceRegistry : IInstanceRegistry
{
    private readonly Dictionary<string, Instance> _instances = new();
    private readonly Dictionary<string, InstanceStatus> _statusOverrides = new();

    public event EventHandler<InstanceRegistryChangedEventArgs> Changed = delegate { };

    public void AddInstance(Instance instance)
    {
        _instances[instance.Id] = instance;
        RaiseChanged(new[] { instance }, Array.Empty<Instance>(), Array.Empty<Instance>());
    }

    public void RemoveInstance(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            _instances.Remove(instanceId);
            RaiseChanged(Array.Empty<Instance>(), new[] { instance }, Array.Empty<Instance>());
        }
    }

    public void UpdateInstance(Instance instance)
    {
        if (_instances.TryGetValue(instance.Id, out var existing))
        {
            _instances[instance.Id] = instance;
            if (!InstanceEquals(existing, instance))
            {
                RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { instance });
            }
        }
    }

    public void SetStatus(string instanceId, InstanceStatus status)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = status };
            _instances[instanceId] = updated;
            _statusOverrides[instanceId] = status;
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public IReadOnlyList<Instance> GetInstances()
    {
        return _instances.Values.OrderBy(i => i.LastSeen).ToList().AsReadOnly();
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
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public void MarkFree(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = InstanceStatus.Ready };
            _instances[instanceId] = updated;
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public void MarkInstanceLost(string instanceId)
    {
        if (_instances.TryGetValue(instanceId, out var instance))
        {
            var updated = instance with { Status = InstanceStatus.InstanceLost };
            _instances[instanceId] = updated;
            RaiseChanged(Array.Empty<Instance>(), Array.Empty<Instance>(), new[] { updated });
        }
    }

    public Instance? GetInstanceByPlayerId(string playerId)
    {
        return _instances.Values.FirstOrDefault(i => i.PlayerId == playerId);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Clear()
    {
        _instances.Clear();
        _statusOverrides.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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
}