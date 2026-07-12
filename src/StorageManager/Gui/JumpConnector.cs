using StorageManager.Auth;
using StorageManager.Connection;
using StorageManager.Ssh;

namespace StorageManager.Gui;

/// <summary>
/// UI-side glue for a jump-host mount: builds the Core collaborators, runs
/// <see cref="JumpConnection"/>, holds the resulting session, and exposes tick
/// (drive the watchdog) and disconnect. The GUI/TUI own the polling timer/loop.
/// </summary>
public sealed class JumpConnector
{
    private readonly Config _config;
    private SshfsJumpMount? _mount;
    private ControlMaster? _master;
    private string? _target;
    private string? _configPath;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public JumpConnector(Config config) => _config = config;

    public JumpSession? Session { get; private set; }
    public bool UsedKerberos => Session?.UsedKerberos ?? false;

    /// <summary>Checks the Kerberos tools are present; returns a remediation or null.</summary>
    public Errors.PreflightResult? KerberosPreflight() =>
        Auth.KerberosPreflight.Check(new KerberosCli(),
            isWindows: OperatingSystem.IsWindows());

    public async Task<JumpConnectOutcome> ConnectAsync(
        string targetHost, string user, string remotePath, string mountTarget,
        string jumpHost, string password, bool readOnly = true, CancellationToken ct = default)
    {
        var runner = SystemProcessRunner.Instance;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        _target = targetHost;
        _master = new ControlMaster(runner);
        _mount = new SshfsJumpMount(runner, targetHost, user, remotePath, mountTarget, readOnly);

        var connection = new JumpConnection(
            _config.BuildRealmMap(),
            new KerberosHelper(new KerberosCli()),
            new SshProfileWriter(),
            _master,
            _configPath,
            () => DateTime.UtcNow);

        var request = new JumpRequest(targetHost, user, remotePath, mountTarget, jumpHost, user, password);
        var outcome = await connection.ConnectAsync(request, _mount, ct);
        Session = outcome.Session;
        return outcome;
    }

    /// <summary>Polls the watchdog once; caller reacts to the result.</summary>
    public Task<MonitorTickResult> TickAsync(CancellationToken ct = default) =>
        Session is { } s ? s.Monitor.TickAsync(ct) : Task.FromResult(MonitorTickResult.Healthy);

    /// <summary>Tears down the mount and master socket. Idempotent and safe to call
    /// concurrently (the timer teardown, the Disconnect button, and window close
    /// can all race); a second call is a no-op.</summary>
    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var mount = _mount;
            var master = _master;
            var target = _target;
            _mount = null;
            _master = null;
            _target = null;
            Session = null;

            if (mount is not null)
                await mount.UnmountAsync(ct);
            if (master is not null && target is not null)
                await master.ExitAsync(target, _configPath, ct);
        }
        finally
        {
            _gate.Release();
        }
    }
}
