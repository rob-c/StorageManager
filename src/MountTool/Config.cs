using System.Text.Json;

namespace MountTool;

public sealed record HostEntry(string Name, bool TwoFactorPam);

public sealed record Config(
    string? Gateway,
    string RemotePath,
    string? MountTarget,
    IReadOnlyList<HostEntry>? Hosts = null,
    bool TwoFactorPam = false,
    int KeepAliveIntervalSeconds = 5,
    int KeepAliveCountMax = 3)
{
    public const string FileName = "mount-config.json";

    public static Config Default { get; } = new(
        null,
        "/home/$USER",
        null,
        [
            new("staff.ph.ed.ac.uk", TwoFactorPam: false),
            new("phcomputeppe01.ph.ed.ac.uk", TwoFactorPam: false),
            new("t3-mw2.ph.ed.ac.uk", TwoFactorPam: false),
            new("lxplus.cern.ch", TwoFactorPam: true),
        ]);

    /// <summary>Selectable hosts; a legacy config with only "gateway" yields a single entry.</summary>
    public IReadOnlyList<HostEntry> HostList =>
        Hosts is { Count: > 0 } ? Hosts
        : Gateway is not null ? [new HostEntry(Gateway, TwoFactorPam)]
        : Default.Hosts!;

    /// <summary>Loads mount-config.json beside the executable if present; throws on malformed content.</summary>
    public static Config Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, FileName);

        if (!File.Exists(path))
            return Default;

        var loaded = JsonSerializer.Deserialize<Config>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException($"{FileName} is empty.");

        if (string.IsNullOrWhiteSpace(loaded.RemotePath))
            throw new InvalidDataException($"{FileName} must set \"remotePath\".");

        if (loaded.Hosts is not { Count: > 0 } && string.IsNullOrWhiteSpace(loaded.Gateway))
            throw new InvalidDataException($"{FileName} must set \"hosts\" or \"gateway\".");

        if (loaded.KeepAliveIntervalSeconds < 1 || loaded.KeepAliveCountMax < 1)
            throw new InvalidDataException($"{FileName} keep-alive settings must be at least 1.");

        return loaded;
    }
}
