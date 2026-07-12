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
    int ServerAliveCountMax = 3);

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

        return
        [
            // Target: reached through the jump, credentials delegated, connection multiplexed.
            Set(p.TargetHost, "HostName", p.TargetHost),
            Set(p.TargetHost, "User", p.TargetUser),
            Set(p.TargetHost, "ProxyJump", p.JumpHost),
            Set(p.TargetHost, "GSSAPIAuthentication", "yes"),
            Set(p.TargetHost, "GSSAPIDelegateCredentials", "yes"),
            Set(p.TargetHost, "ControlMaster", "auto"),
            Set(p.TargetHost, "ControlPath", p.ControlPath),
            Set(p.TargetHost, "ControlPersist", p.ControlPersist),
            Set(p.TargetHost, "ServerAliveInterval", p.ServerAliveInterval.ToString()),
            Set(p.TargetHost, "ServerAliveCountMax", p.ServerAliveCountMax.ToString()),

            // Jump host: same auth/keepalive posture.
            Set(p.JumpHost, "HostName", p.JumpHost),
            Set(p.JumpHost, "User", p.JumpUser),
            Set(p.JumpHost, "GSSAPIAuthentication", "yes"),
            Set(p.JumpHost, "GSSAPIDelegateCredentials", "yes"),
            Set(p.JumpHost, "ServerAliveInterval", p.ServerAliveInterval.ToString()),
            Set(p.JumpHost, "ServerAliveCountMax", p.ServerAliveCountMax.ToString()),
        ];
    }

    /// <summary>Applies the profile to the config file, writing a backup first. Idempotent.</summary>
    public FixOutcome Apply(string configPath, JumpProfile p)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        return _fixer.Apply(configPath, BuildFixes(p), dryRun: false);
    }
}
