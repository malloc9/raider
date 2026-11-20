namespace Raider.Core.Models;

/// <summary>
/// Represents a game build version.
/// </summary>
public sealed record GameBuild(
    string Version,
    DateTimeOffset ReleasedAt
)
{
    public static GameBuild Unknown => new("unknown", DateTimeOffset.MinValue);
    
    public bool Matches(GameBuild other) => Version == other.Version;
}