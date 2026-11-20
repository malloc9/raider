namespace Raider.Core.Services;

using System.Net.Http;
using System.Security.Cryptography;

/// <summary>
/// The BepInEx build Raider installs into the game folder. Raid is an IL2CPP game, so this is
/// BepInEx 6's Unity IL2CPP edition. Keep <see cref="Version"/> equal to the BepInEx.Unity.IL2CPP
/// package version in Raider.Plugin.csproj.
/// </summary>
public static class BepInExBuild
{
    public const string Version = "6.0.0-be.793";
    public const string FileName = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793.zip";
    public const string DownloadUrl = "https://builds.bepinex.dev/projects/bepinex_be/793/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.793%2Bdbc82f0.zip";
    public const string Sha256 = "5b63d7d0e92a2448ccd13b738ff95ff1cc1026c6d718555d4df8e6c824953ac4";
}

/// <summary>
/// Supplies the BepInEx package zip to install.
/// </summary>
public interface IBepInExPackageProvider
{
    /// <summary>Returns the local path of a verified copy of <see cref="BepInExBuild.FileName"/>.</summary>
    Task<string> GetPackageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Downloads the pinned BepInEx build once into %LOCALAPPDATA%\Raider\cache and checks its SHA-256,
/// so the player installs nothing by hand (ADR 0001).
/// </summary>
public sealed class DownloadingBepInExPackageProvider : IBepInExPackageProvider
{
    private readonly string _cacheDirectory;
    private readonly HttpClient _httpClient;

    public DownloadingBepInExPackageProvider(string? cacheDirectory = null, HttpClient? httpClient = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Raider", "cache");
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<string> GetPackageAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var packagePath = Path.Combine(_cacheDirectory, BepInExBuild.FileName);
        if (File.Exists(packagePath) && await HasExpectedHashAsync(packagePath, cancellationToken))
        {
            return packagePath;
        }

        Directory.CreateDirectory(_cacheDirectory);
        progress?.Report($"Downloading BepInEx {BepInExBuild.Version}...");

        var tempPath = packagePath + ".download";
        using (var response = await _httpClient.GetAsync(BepInExBuild.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var file = File.Create(tempPath);
            await response.Content.CopyToAsync(file, cancellationToken);
        }

        if (!await HasExpectedHashAsync(tempPath, cancellationToken))
        {
            File.Delete(tempPath);
            throw new InvalidDataException($"Downloaded BepInEx package does not match the expected SHA-256 ({BepInExBuild.Sha256})");
        }

        File.Move(tempPath, packagePath, overwrite: true);
        return packagePath;
    }

    private static async Task<bool> HasExpectedHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).Equals(BepInExBuild.Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
