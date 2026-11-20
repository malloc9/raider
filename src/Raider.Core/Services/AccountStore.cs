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
/// Only the Plarium login (an email/username used to pick a remembered login) is stored, never a password.
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
        return settings?.Accounts ?? new List<Account>();
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
