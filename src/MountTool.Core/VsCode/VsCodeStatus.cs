namespace MountTool.VsCode;

/// <summary>The remote target VS Code should be set up for.</summary>
public sealed record VsCodeTarget(string Alias, string HostName, string User, string? ProxyJump = null);

/// <summary>A single check in a VS Code setup verification, for display.</summary>
public sealed record VsCodeCheck(string Label, bool Ok, string Detail);

/// <summary>The result of verifying a VS Code remote setup.</summary>
public sealed record VsCodeStatus(IReadOnlyList<VsCodeCheck> Checks)
{
    public bool AllOk => Checks.All(c => c.Ok);
}

/// <summary>The result of running setup: which steps ran and any backups written.</summary>
public sealed record VsCodeSetupResult(
    IReadOnlyList<string> InstalledExtensions,
    bool HostConfigured,
    string? SshBackupPath,
    bool SettingsConfigured,
    string? SettingsBackupPath,
    string? Error);
