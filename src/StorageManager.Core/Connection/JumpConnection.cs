using StorageManager.Auth;
using StorageManager.Errors;
using StorageManager.Ssh;

namespace StorageManager.Connection;

/// <summary>What to mount, and the jump host to reach it through.</summary>
public sealed record JumpRequest(
    string TargetHost,
    string TargetUser,
    string RemotePath,
    string MountTarget,
    string JumpHost,
    string JumpUser,
    string Password,
    bool UseKerberos = false);

/// <summary>An established jump connection the caller drives (poll <see cref="Monitor"/> on a timer).</summary>
public sealed record JumpSession(
    ConnectionMonitor Monitor,
    string Target,
    string SshConfigPath,
    bool UsedKerberos);

/// <summary>Result of attempting a jump connection.</summary>
public sealed record JumpConnectOutcome(JumpSession? Session, string? Error, bool UsedKerberos)
{
    public bool Success => Session is not null;
}

/// <summary>The mount half of a jump connection (kept behind an interface so orchestration is testable).</summary>
public interface IJumpMount
{
    /// <summary>True while the target is mounted. Async so liveness checks never block the UI thread.</summary>
    Task<bool> IsMountedAsync(CancellationToken ct = default);
    /// <summary>Mounts; returns null on success or a user-facing error.</summary>
    Task<string?> MountAsync(CancellationToken ct = default);
    Task UnmountAsync(CancellationToken ct = default);
}

/// <summary>
/// Orchestrates a jump-host mount: turn the password into a forwardable/addressless
/// Kerberos ticket, write the target+jump ssh_config profile, bring up the shared
/// master (GSSAPI through the jump), mount over it, and hand back a wired
/// <see cref="ConnectionMonitor"/>. Auto-reconnect relies on the ticket, so it is
/// only offered on the Kerberos path.
/// </summary>
public sealed class JumpConnection(
    RealmMap realmMap,
    KerberosHelper kerberos,
    SshProfileWriter profileWriter,
    ControlMaster master,
    string sshConfigPath,
    Func<DateTime> clock,
    IReadOnlyDictionary<string, string>? sshEnvironment = null,
    MonitorOptions? monitorOptions = null)
{
    public async Task<JumpConnectOutcome> ConnectAsync(JumpRequest req, IJumpMount mount, CancellationToken ct = default)
    {
        // 0. Reject unsafe host/user strings up front (ssh_config injection defense).
        if (!SshName.IsValidHost(req.TargetHost) || !SshName.IsValidHost(req.JumpHost)
            || !SshName.IsValidUser(req.TargetUser) || !SshName.IsValidUser(req.JumpUser))
            return new JumpConnectOutcome(null,
                "The host or username contains characters that aren't allowed. " +
                "Use plain host names and usernames.", false);

        // 1. Auth. Kerberos only when the user opted in AND a realm/tools exist; otherwise
        //    password on both hops (answered non-interactively by the askpass environment).
        var usedKerberos = false;
        if (req.UseKerberos)
        {
            var realm = realmMap.RealmFor(req.TargetHost);
            if (realm is not null && kerberos.Status().ToolsAvailable)
            {
                var after = kerberos.Authenticate(
                    $"{req.TargetUser}@{realm}", req.Password,
                    alsoAklog: true, forwardable: true, addressless: true);
                if (!after.HasValidTicket)
                    return new JumpConnectOutcome(null,
                        "Kerberos sign-in failed. Check your username and password, then try again.", false);
                usedKerberos = true;
            }
        }

        // 2. Write the ssh_config profile (target + jump blocks), backed up first.
        profileWriter.Apply(sshConfigPath,
            new JumpProfile(req.TargetHost, req.TargetUser, req.JumpHost, req.JumpUser));

        // 3. Bring up the shared master through the jump. Kerberos → non-interactive
        //    (BatchMode); password → let SSH_ASKPASS answer the prompts.
        var batchMode = usedKerberos;
        var established = await master.EstablishAsync(req.TargetHost, sshConfigPath, sshEnvironment, batchMode, ct);
        if (!established.Ok)
        {
            var friendly = ErrorTranslator.Translate(established.Stderr, established.ExitCode, twoFactor: false);
            return new JumpConnectOutcome(null,
                $"{friendly.Headline} {friendly.Guidance}".Trim(), usedKerberos);
        }

        // 4. Mount over the master.
        var mountError = await mount.MountAsync(ct);
        if (mountError is not null)
        {
            await master.ExitAsync(req.TargetHost, sshConfigPath, ct);
            var friendly = ErrorTranslator.Translate(mountError, 1, twoFactor: false);
            return new JumpConnectOutcome(null,
                friendly.Guidance.Length > 0 ? $"{friendly.Headline} {friendly.Guidance}" : mountError, usedKerberos);
        }

        // 5. Watchdog: healthy = master alive AND mount present; reconnect uses the ticket.
        var monitor = new ConnectionMonitor(
            checkHealthy: async c => await master.IsAliveAsync(req.TargetHost, sshConfigPath, c)
                                     && await mount.IsMountedAsync(c),
            teardown: async c =>
            {
                await mount.UnmountAsync(c);
                await master.ExitAsync(req.TargetHost, sshConfigPath, c);
            },
            reconnect: async c =>
            {
                var r = await master.EstablishAsync(req.TargetHost, sshConfigPath, sshEnvironment, batchMode, c);
                return r.Ok && await mount.MountAsync(c) is null;
            },
            // Auto-reconnect relies on the Kerberos ticket; the password path
            // requires a manual reconnect (we don't keep the password around).
            hasValidTicket: usedKerberos ? () => kerberos.Status().HasValidTicket : () => false,
            clock: clock,
            options: monitorOptions);

        return new JumpConnectOutcome(
            new JumpSession(monitor, req.TargetHost, sshConfigPath, usedKerberos), null, usedKerberos);
    }
}
