using MountTool.Doctor;

namespace MountTool.VsCode;

/// <summary>
/// One-button VS Code remote setup and verification. Installs the Remote
/// Development extensions, writes an ssh_config Host block (with ProxyJump for
/// jump hosts) via the tested <see cref="ConfigFixer"/>, and merges the
/// Remote-SSH keys into VS Code's settings.json — each step backed up first.
/// </summary>
public sealed class VsCodeSetup
{
    /// <summary>Extensions installed by setup. The pack is a superset; remote-ssh is the key indicator.</summary>
    public static IReadOnlyList<string> DesiredExtensions { get; } =
    [
        "ms-vscode-remote.remote-ssh",
        "ms-vscode-remote.remote-ssh-edit",
        "ms-vscode-remote.vscode-remote-extensionpack",
    ];

    private readonly IVsCodeCli _cli;
    private readonly IClock _clock;
    private readonly SshConfigParser _parser = new();
    private readonly EffectiveConfigResolver _resolver = new();

    public VsCodeSetup(IVsCodeCli cli, IClock? clock = null)
    {
        _cli = cli;
        _clock = clock ?? SystemClock.Instance;
    }

    public VsCodeSetupResult Setup(VsCodeTarget target, string sshConfigPath, string settingsPath)
    {
        if (_cli.ExecutablePath is null)
            return new VsCodeSetupResult([], false, null, false, null,
                "The VS Code command line ('code') was not found. Install VS Code, then run " +
                "\"Shell Command: Install 'code' command in PATH\" from its Command Palette.");

        var installed = new List<string>();
        var present = _cli.ListExtensions().ToHashSet();
        foreach (var ext in DesiredExtensions)
        {
            if (present.Contains(ext.ToLowerInvariant()))
                continue;
            if (_cli.InstallExtension(ext))
                installed.Add(ext);
        }

        var (hostOk, sshBackup) = WriteHostBlock(target, sshConfigPath);
        var (settingsOk, settingsBackup) = WriteSettings(settingsPath);

        return new VsCodeSetupResult(installed, hostOk, sshBackup, settingsOk, settingsBackup, null);
    }

    private (bool Ok, string? Backup) WriteHostBlock(VsCodeTarget target, string sshConfigPath)
    {
        var fixes = new List<SuggestedFix>
        {
            new("VS Code host", "HostName", target.HostName, target.Alias, FixKind.SetOrReplace),
            new("VS Code host", "User", target.User, target.Alias, FixKind.SetOrReplace),
        };
        if (!string.IsNullOrWhiteSpace(target.ProxyJump))
            fixes.Add(new("VS Code host", "ProxyJump", target.ProxyJump, target.Alias, FixKind.SetOrReplace));

        Directory.CreateDirectory(Path.GetDirectoryName(sshConfigPath)!);
        var outcome = new ConfigFixer(_clock).Apply(sshConfigPath, fixes, dryRun: false);
        return (true, outcome.BackupPath);
    }

    private (bool Ok, string? Backup) WriteSettings(string settingsPath)
    {
        try
        {
            var existing = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            if (VsCodeSettings.IsConfigured(existing))
                return (true, null);

            string? backup = null;
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            if (File.Exists(settingsPath))
            {
                backup = settingsPath + ".bak-" + _clock.UtcNow.ToString("yyyyMMdd-HHmmss");
                File.Copy(settingsPath, backup, overwrite: true);
            }
            File.WriteAllText(settingsPath, VsCodeSettings.Merge(existing));
            return (true, backup);
        }
        catch
        {
            return (false, null);
        }
    }

    public VsCodeStatus Verify(VsCodeTarget target, string sshConfigPath, string settingsPath)
    {
        var checks = new List<VsCodeCheck>();

        checks.Add(_cli.ExecutablePath is { } path
            ? new VsCodeCheck("VS Code CLI", true, path)
            : new VsCodeCheck("VS Code CLI", false, "'code' not found on PATH or in standard locations"));

        var extensions = _cli.ExecutablePath is null ? [] : _cli.ListExtensions();
        var hasRemoteSsh = extensions.Contains("ms-vscode-remote.remote-ssh");
        checks.Add(new VsCodeCheck("Remote-SSH extension", hasRemoteSsh,
            hasRemoteSsh ? "installed" : "not installed"));

        var (hostOk, hostDetail) = VerifyHost(target, sshConfigPath);
        checks.Add(new VsCodeCheck($"ssh_config host '{target.Alias}'", hostOk, hostDetail));

        var settingsText = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var settingsOk = VsCodeSettings.IsConfigured(settingsText);
        checks.Add(new VsCodeCheck("VS Code Remote-SSH settings", settingsOk,
            settingsOk ? "showLoginTerminal + timeout set" : "not configured (2FA prompts may not appear)"));

        return new VsCodeStatus(checks);
    }

    private (bool Ok, string Detail) VerifyHost(VsCodeTarget target, string sshConfigPath)
    {
        if (!File.Exists(sshConfigPath))
            return (false, "no ~/.ssh/config yet");

        var config = _parser.Parse(sshConfigPath);
        var effective = _resolver.Resolve(config, target.Alias);

        if (!effective.Values.TryGetValue("HostName", out var hostName))
            return (false, $"no Host block resolves a HostName for '{target.Alias}'");

        if (!string.IsNullOrWhiteSpace(target.ProxyJump)
            && !effective.Values.ContainsKey("ProxyJump"))
            return (false, $"HostName {hostName} set, but ProxyJump is missing");

        return (true, effective.Values.TryGetValue("ProxyJump", out var jump)
            ? $"HostName {hostName} via {jump}"
            : $"HostName {hostName}");
    }
}
