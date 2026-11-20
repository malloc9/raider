namespace Raider.Core.Models;

/// <summary>
/// One Raid: Shadow Legends player profile, reached by logging into Plarium Play with one Plarium login.
/// </summary>
public sealed record Account(
    string Id,
    string PlariumLogin,
    string DisplayName,
    Routine Routine
);