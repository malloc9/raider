namespace Raider.Core.Services;


using System.Text.Json;
using Raider.Core.Models;

/// <summary>
/// Finds Raid: Shadow Legends through Plarium Play's game storage record, then its default install folder.
/// </summary>
/// <remarks>
/// Plarium Play (10.x) keeps its data in %LOCALAPPDATA%\PlariumPlay. Its game storage record is
/// <c>gamestorage.gsfn</c>, a JSON file such as:
/// <code>
/// {"InstalledGames":{"101":{"Id":101,"InstallationPath":"C:\\...\\PlariumPlay\\StandAloneApps",
///   "InsalledGames":{"raid-shadow-legends":"157786"}, ...}}}
/// </code>
/// ("InsalledGames" is Plarium's own spelling.) The game itself is in
/// <c>&lt;InstallationPath&gt;\raid-shadow-legends\build\Raid.exe</c>.
/// </remarks>
public sealed class GameDetectionService : IGameDetectionService
{
    internal const string PlariumPlayFolderName = "PlariumPlay";
    internal const string GameStorageFileName = "gamestorage.gsfn";
    internal const string RaidGameKey = "raid-shadow-legends";
    internal const string BuildFolderName = "build";

    private readonly string _localAppData;

    /// <param name="localAppDataPath">Root used instead of %LOCALAPPDATA% (for tests).</param>
    public GameDetectionService(string? localAppDataPath = null)
    {
        _localAppData = localAppDataPath ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    }

    private string PlariumPlayRoot => Path.Combine(_localAppData, PlariumPlayFolderName);

    public async Task<GameInstallation?> FindGameInstallationAsync(CancellationToken cancellationToken = default)
    {
        foreach (var candidate in GetCandidateGamePaths())
        {
            if (await ValidateGameInstallationAsync(candidate, cancellationToken))
            {
                return new GameInstallation(
                    GamePath: candidate,
                    ExecutablePath: Path.Combine(candidate, "Raid.exe"),
                    DataPath: Path.Combine(candidate, "Raid_Data"),
                    IsPlariumPlay: true,
                    PlariumPlayVersion: GetPlariumPlayVersion());
            }
        }

        return null;
    }

    public Task<bool> ValidateGameInstallationAsync(string gamePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return Task.FromResult(false);

        var exePath = Path.Combine(gamePath, "Raid.exe");
        var dataPath = Path.Combine(gamePath, "Raid_Data");
        if (!File.Exists(exePath) || !Directory.Exists(dataPath))
            return Task.FromResult(false);

        // IL2CPP build (il2cpp_data) or Mono build (Managed)
        return Task.FromResult(
            Directory.Exists(Path.Combine(dataPath, "il2cpp_data")) ||
            Directory.Exists(Path.Combine(dataPath, "Managed")));
    }

    /// <summary>
    /// Folders that may hold Raid.exe, most authoritative first.
    /// </summary>
    private IEnumerable<string> GetCandidateGamePaths()
    {
        foreach (var installationPath in ReadGameStorageInstallationPaths())
        {
            yield return Path.Combine(installationPath, RaidGameKey, BuildFolderName);
            yield return Path.Combine(installationPath, RaidGameKey);
        }

        // Default location when the record is missing or unreadable
        yield return Path.Combine(PlariumPlayRoot, "StandAloneApps", RaidGameKey, BuildFolderName);
    }

    /// <summary>
    /// Installation folders of every Plarium Play game entry that lists Raid.
    /// </summary>
    private IEnumerable<string> ReadGameStorageInstallationPaths()
    {
        var storagePath = Path.Combine(PlariumPlayRoot, GameStorageFileName);
        if (!File.Exists(storagePath))
            return Array.Empty<string>();

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(storagePath));
            if (!doc.RootElement.TryGetProperty("InstalledGames", out var installedGames) ||
                installedGames.ValueKind != JsonValueKind.Object)
            {
                return Array.Empty<string>();
            }

            var paths = new List<string>();
            foreach (var entry in installedGames.EnumerateObject())
            {
                if (entry.Value.ValueKind == JsonValueKind.Object &&
                    entry.Value.TryGetProperty("InstallationPath", out var pathElement) &&
                    pathElement.GetString() is { Length: > 0 } path &&
                    ListsRaid(entry.Value))
                {
                    paths.Add(path);
                }
            }

            return paths;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // Unreadable record: fall back to the default location
            return Array.Empty<string>();
        }
    }

    private static bool ListsRaid(JsonElement gameEntry)
    {
        // Plarium writes "InsalledGames"; accept the correct spelling too in case they fix it
        foreach (var name in new[] { "InsalledGames", "InstalledGames" })
        {
            if (gameEntry.TryGetProperty(name, out var games) &&
                games.ValueKind == JsonValueKind.Object &&
                games.TryGetProperty(RaidGameKey, out _))
            {
                return true;
            }
        }

        return false;
    }

    private string? GetPlariumPlayVersion()
    {
        // Plarium Play keeps one folder per installed version, e.g. "10.18.0-0.0.0"; its exe reports 1.0.0.0
        if (!Directory.Exists(PlariumPlayRoot))
            return null;

        return Directory.EnumerateDirectories(PlariumPlayRoot)
            .Select(dir => Path.GetFileName(dir).Split('-')[0])
            .Select(name => Version.TryParse(name, out var version) ? version : null)
            .Where(version => version != null)
            .Max()
            ?.ToString();
    }
}
