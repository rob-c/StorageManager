using StorageManager.Doctor;

namespace StorageManager.Ssh;

/// <summary>Describes a jump-host connection to write into ssh_config.</summary>
public sealed record JumpProfile(
    string TargetHost,
    string TargetUser,
    string JumpHost,
    string JumpUser,
    // %C hashes %l%h%p%r%j — folds in the ProxyJump so distinct jumps get distinct
    // sockets, and keeps the path well under the 108-byte AF_UNIX limit.
    string ControlPath = "~/.ssh/cm/%C",
    string ControlPersist = "30s",
    int ServerAliveInterval = 5,
    int ServerAliveCountMax = 3,
    bool UseKerberos = false);

/// <summary>
/// Writes the target + jump Host blocks a jump-host mount needs into
/// <c>~/.ssh/config</c> using the span-scoped <see cref="ConfigFixer"/> (so the
/// file is backed up and comments preserved). Because the blocks live in the
/// user's own config — with ControlMaster/ControlPath/ControlPersist — the
/// resulting master socket is shared with the user's own <c>ssh</c>.
/// </summary>
public sealed class SshProfileWriter(IClock? clock = null)
{
    private readonly ConfigFixer _fixer = new(clock);

    public IReadOnlyList<SuggestedFix> BuildFixes(JumpProfile p)
    {
        SuggestedFix Set(string host, string keyword, string value) =>
            new($"jump profile: {host}", keyword, value, host, FixKind.SetOrReplace);
        SuggestedFix Remove(string host, string keyword) =>
            new($"jump profile: {host}", keyword, null, host, FixKind.RemoveLine);

        var fixes = new List<SuggestedFix>
        {
            // Target: reached through the jump, connection multiplexed.
            Set(p.TargetHost, "HostName", p.TargetHost),
            Set(p.TargetHost, "User", p.TargetUser),
            Set(p.TargetHost, "ProxyJump", p.JumpHost),
            Set(p.TargetHost, "ControlMaster", "auto"),
            Set(p.TargetHost, "ControlPath", p.ControlPath),
            Set(p.TargetHost, "ControlPersist", p.ControlPersist),
            Set(p.TargetHost, "ServerAliveInterval", p.ServerAliveInterval.ToString()),
            Set(p.TargetHost, "ServerAliveCountMax", p.ServerAliveCountMax.ToString()),

            Set(p.JumpHost, "HostName", p.JumpHost),
            Set(p.JumpHost, "User", p.JumpUser),
            Set(p.JumpHost, "ServerAliveInterval", p.ServerAliveInterval.ToString()),
            Set(p.JumpHost, "ServerAliveCountMax", p.ServerAliveCountMax.ToString()),
        };

        if (p.UseKerberos)
        {
            // Delegate the ticket only to the target — with ProxyJump the target hop
            // is end-to-end, so the intermediate jump never needs (or gets) the TGT.
            fixes.Add(Set(p.TargetHost, "GSSAPIAuthentication", "yes"));
            fixes.Add(Set(p.TargetHost, "GSSAPIDelegateCredentials", "yes"));
            fixes.Add(Set(p.JumpHost, "GSSAPIAuthentication", "yes"));
        }
        else
        {
            // Kerberos is off app-wide: scrub any GSSAPI directives a previous
            // Kerberos-mode run left behind so ssh never attempts it.
            fixes.Add(Remove(p.TargetHost, "GSSAPIAuthentication"));
            fixes.Add(Remove(p.TargetHost, "GSSAPIDelegateCredentials"));
            fixes.Add(Remove(p.JumpHost, "GSSAPIAuthentication"));
            fixes.Add(Remove(p.JumpHost, "GSSAPIDelegateCredentials"));
        }

        return fixes;
    }

    /// <summary>Applies the profile to the config file, writing a backup first. Idempotent.
    /// Validates host/user names (defense in depth against ssh_config injection) and
    /// creates the ControlPath parent directory, which OpenSSH will not create itself.</summary>
    public FixOutcome Apply(string configPath, JumpProfile p)
    {
        SshName.Validate(p.TargetHost, p.TargetUser, p.JumpHost, p.JumpUser);
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        EnsureControlPathDirectory(p.ControlPath);
        return _fixer.Apply(configPath, BuildFixes(p), dryRun: false);
    }

    /// <summary>Creates the directory that holds the ControlMaster sockets (e.g. ~/.ssh/cm).</summary>
    private static void EnsureControlPathDirectory(string controlPath)
    {
        try
        {
            var expanded = controlPath.StartsWith('~')
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + controlPath[1..]
                : controlPath;
            // Drop the final token component (e.g. %C) to get the parent directory.
            var dir = Path.GetDirectoryName(expanded);
            if (string.IsNullOrEmpty(dir))
                return;
            if (OperatingSystem.IsWindows())
                Directory.CreateDirectory(dir);
            else
                Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        catch
        {
            // If we can't pre-create it, ssh will surface a clear bind error.
        }
    }
}
