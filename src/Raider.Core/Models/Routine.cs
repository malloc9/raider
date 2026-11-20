namespace Raider.Core.Models;

/// <summary>
/// The ordered list of Chores configured for one Account.
/// </summary>
public sealed record Routine(
    IReadOnlyList<Chore> Chores
);