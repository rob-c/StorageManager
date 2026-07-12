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

    public JumpConnector(Config config) => _config = config;

    public JumpSession? Session { get; private set; }
    public bool UsedKerberos => Session?.UsedKerberos ?? false;
    public bool IsMounted => _mount?.IsMounted ?? false;

    /// <summary>Checks the Kerberos tools are present; returns a remediation or null.</summary>
    public Errors.PreflightResult? KerberosPreflight() =>
        Auth.KerberosPreflight.Check(new KerberosCli(),
            isWindows: OperatingSystem.IsWindows());

    public async Task<JumpConnectOutcome> ConnectAsync(
        string targetHost, string user, string remotePath, string mountTarget,
        string jumpHost, string password, CancellationToken ct = default)
    {
        var runner = SystemProcessRunner.Instance;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config");
        _target = targetHost;
        _master = new ControlMaster(runner);
        _mount = new SshfsJumpMount(runner, targetHost, user, remotePath, mountTarget);

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

    public async Task DisconnectAsync(CancellationToken ct = default)
    {
        if (_mount is { } m)
            await m.UnmountAsync(ct);
        if (_master is { } master && _target is { } t)
            await master.ExitAsync(t, _configPath, ct);
        Session = null;
        _mount = null;
        _master = null;
    }
}
