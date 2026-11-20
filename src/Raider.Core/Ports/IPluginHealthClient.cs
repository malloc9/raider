namespace Raider.Core.Ports;

using Raider.Core.Models;

/// <summary>
/// Client for communicating with the plugin's health endpoint.
/// </summary>
public interface IPluginHealthClient
{
    /// <summary>
    /// Gets the health status from the plugin.
    /// </summary>
    Task<PluginHealth?> GetHealthAsync(string token, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Checks if the plugin is reachable and healthy.
    /// </summary>
    Task<bool> IsHealthyAsync(string token, CancellationToken cancellationToken = default);
}