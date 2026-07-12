namespace StorageManager.Settings;

/// <summary>
/// Remembered form values from the last successful mount. No password is ever
/// stored. Custom hosts/paths are the values a user entered via "Other…".
/// UseKerberos is the app-wide Kerberos switch (the single UI tickbox): when
/// false, nothing in the app attempts Kerberos/GSSAPI authentication.
/// </summary>
public sealed record UserSettings(
    string? Username = null,
    string? HostName = null,
    string? RemotePathTemplate = null,
    string? MountTarget = null,
    IReadOnlyList<string>? CustomHosts = null,
    IReadOnlyList<string>? CustomPaths = null,
    bool UseKerberos = false)
{
    public static UserSettings Empty { get; } = new();

    public IReadOnlyList<string> Hosts => CustomHosts ?? [];
    public IReadOnlyList<string> Paths => CustomPaths ?? [];
}
