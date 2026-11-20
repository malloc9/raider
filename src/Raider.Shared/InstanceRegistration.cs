namespace Raider.Shared;

using System.Text.Json;

/// <summary>
/// How a running Instance's plugin tells Raider where to reach it: one JSON file per Raid process,
/// <c>%LOCALAPPDATA%\Raider\instances\&lt;pid&gt;.json</c>. Compiled into both the plugin and Raider.Core.
/// </summary>
public sealed record InstanceRegistration(int Pid, int Port, DateTimeOffset ProcessStart)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>The folder every plugin writes its registration to.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Raider",
        "instances");

    /// <summary>The plugin's HTTP base address.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri BaseUrl => new($"http://localhost:{Port}/raider/");

    public static string FilePath(string directory, int pid) => Path.Combine(directory, $"{pid}.json");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Parses a registration, or returns null when the text is not one.</summary>
    public static InstanceRegistration? FromJson(string json)
    {
        try
        {
            var registration = JsonSerializer.Deserialize<InstanceRegistration>(json, JsonOptions);
            return registration is { Pid: > 0, Port: > 0 } ? registration : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes this registration to <paramref name="directory"/>, replacing any earlier file for the same pid.</summary>
    public void WriteTo(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = FilePath(directory, Pid);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, ToJson());
        File.Move(temporaryPath, path, overwrite: true);
    }

    /// <summary>Removes the registration file for <paramref name="pid"/>, if there is one.</summary>
    public static void Delete(string directory, int pid)
    {
        try
        {
            File.Delete(FilePath(directory, pid));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Reads every registration in <paramref name="directory"/>, skipping files that are not one.</summary>
    public static IReadOnlyList<InstanceRegistration> ReadAll(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<InstanceRegistration>();
        }

        var registrations = new List<InstanceRegistration>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                if (FromJson(File.ReadAllText(path)) is { } registration)
                {
                    registrations.Add(registration);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return registrations;
    }
}
