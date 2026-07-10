using System.Text.Json;

namespace MountTool;

public sealed record Config(string Gateway, string RemotePath, string? MountTarget)
{
    public const string FileName = "mount-config.json";

    public static Config Default { get; } =
        new("staff.ph.ed.ac.uk", "/storage/datastore-group/PPE", null);

    /// <summary>Loads mount-config.json beside the executable if present; throws on malformed content.</summary>
    public static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        if (!File.Exists(path))
            return Default;

        var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{FileName} is empty.");

        if (string.IsNullOrWhiteSpace(loaded.Gateway) || string.IsNullOrWhiteSpace(loaded.RemotePath))
            throw new InvalidDataException($"{FileName} must set \"gateway\" and \"remotePath\".");

        return loaded;
    }
}
