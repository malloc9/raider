namespace Raider.Core.Services;

using System.Text.Json;
using System.Text.Json.Serialization;
using Raider.Core.Models;

/// <summary>
/// Persists the player's Accounts and their Routines.
/// </summary>
public interface IAccountStore
{
    Task<IReadOnlyList<Account>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken = default);
}

/// <summary>
/// Stores Accounts as JSON in roaming app data (ADR 0002): %APPDATA%\Raider\raider-settings.json.
/// Account format: Id (player ID), DisplayName, Routine, LastRunGameDay, LastOutcome.
/// Old entries with PlariumLogin are discarded on load.
/// </summary>
public sealed class JsonAccountStore : IAccountStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;

    public JsonAccountStore(string? filePath = null)
    {
        _filePath = filePath ?? DefaultFilePath;
    }

    public static string DefaultFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Raider",
        "raider-settings.json");

    public async Task<IReadOnlyList<Account>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<Account>();
        }

        await using var stream = File.OpenRead(_filePath);
        var settings = await JsonSerializer.DeserializeAsync<SettingsFile>(stream, JsonOptions, cancellationToken);
        
        if (settings?.Accounts == null)
        {
            return Array.Empty<Account>();
        }

        // Filter out old format entries (those with PlariumLogin) and migrate
        var migrated = new List<Account>();
        foreach (var account in settings.Accounts)
        {
            if (account.PlariumLogin != null)
            {
                // Old format - discard as per requirement
                continue;
            }
            migrated.Add(account with 
            { 
                // Ensure default values for new fields
                LastRunGameDay = account.LastRunGameDay,
                LastOutcome = account.LastOutcome
            });
        }

        return migrated;
    }

    public async Task SaveAsync(IReadOnlyList<Account> accounts, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Write to a temp file then replace, so a crash never leaves a half-written settings file
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, new SettingsFile { Accounts = accounts.ToList() }, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private sealed class SettingsFile
    {
        public List<Account> Accounts { get; set; } = new();
    }
}