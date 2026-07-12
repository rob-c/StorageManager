namespace MountTool.Settings;

/// <summary>
/// Remembered form values from the last successful mount. No password is ever
/// stored. Custom hosts/paths are the values a user entered via "Other…".
/// </summary>
public sealed record UserSettings(
    string? Username = null,
    string? HostName = null,
    string? RemotePathTemplate = null,
    string? MountTarget = null,
    IReadOnlyList<string>? CustomHosts = null,
    IReadOnlyList<string>? CustomPaths = null)
{
    public static UserSettings Empty { get; } = new();

    public IReadOnlyList<string> Hosts => CustomHosts ?? [];
    public IReadOnlyList<string> Paths => CustomPaths ?? [];
}
