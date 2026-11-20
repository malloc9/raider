namespace Raider.Core.Services;

using System.Diagnostics;
using Raider.Core.Models;

/// <summary>
/// Detects running Raid: Shadow Legends instances by finding Raid.exe processes.
/// Each instance is matched to an Account by querying its plugin health endpoint.
/// </summary>
public sealed class InstanceDetectionService : IInstanceDetectionService
{
    private const string RaidProcessName = "Raid";

    public async Task<IReadOnlyList<Instance>> DetectInstancesAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken = default)
    {
        var processes = Process.GetProcessesByName(RaidProcessName);
        var instances = new List<Instance>();

        foreach (var process in processes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var instance = await CreateInstanceFromProcessAsync(process, accounts, cancellationToken);
                if (instance != null)
                {
                    instances.Add(instance);
                }
            }
            catch (Exception)
            {
                // Ignore processes we can't inspect (access denied, etc.)
            }
        }

        // Mark duplicates (multiple instances for the same Account)
        MarkDuplicateInstances(instances);

        return instances;
    }

    private async Task<Instance?> CreateInstanceFromProcessAsync(Process process, IReadOnlyList<Account> accounts, CancellationToken cancellationToken)
    {
        if (process.MainWindowHandle == IntPtr.Zero)
            return null;

        string gamePath;
        try
        {
            gamePath = process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            // Can't access process info (e.g., elevated process)
            return null;
        }

        if (string.IsNullOrEmpty(gamePath))
            return null;

        // Try to get the plugin health endpoint to find the Account
        // The plugin runs on a port that we need to discover
        // For now, we'll use the default port and token from the game's config
        // In a real implementation, we'd need to read the token from the game's config file
        var accountId = await TryGetAccountIdFromPluginAsync(process, cancellationToken);

        // Match with known accounts
        var matchedAccount = accounts.FirstOrDefault(a => string.Equals(a.Id, accountId, StringComparison.OrdinalIgnoreCase));

        return new Instance(
            Id: Guid.NewGuid().ToString("N")[..12],
            ProcessId: process.Id,
            GamePath: gamePath,
            AccountId: matchedAccount?.Id,
            State: InstanceState.Idle,
            LastRunStartedAt: null,
            LastRunCompletedAt: null,
            LastRunStatus: null,
            LastRunMessage: null);
    }

    private async Task<string?> TryGetAccountIdFromPluginAsync(Process process, CancellationToken cancellationToken)
    {
        // TODO: In a real implementation, we would:
        // 1. Read the plugin config file from the game's BepInEx/config folder to get the token and port
        // 2. Query the plugin's health endpoint
        // 3. The health endpoint would need to return the current Plarium login
        // 4. Match that login to an Account
        
        // For now, return null (unknown account)
        await Task.CompletedTask;
        return null;
    }

    private void MarkDuplicateInstances(List<Instance> instances)
    {
        var accountCounts = instances
            .Where(i => i.AccountId != null)
            .GroupBy(i => i.AccountId!)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var instance in instances)
        {
            if (instance.AccountId != null && accountCounts.GetValueOrDefault(instance.AccountId, 0) > 1)
            {
                // Create a new instance with Duplicate state
                var index = instances.IndexOf(instance);
                instances[index] = instance with { State = InstanceState.Duplicate };
            }
        }
    }
}