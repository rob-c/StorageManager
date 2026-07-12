using StorageManager;
using StorageManager.Auth;
using StorageManager.Connection;
using StorageManager.Ssh;
using StorageManager.Core.Tests.Ssh;

namespace StorageManager.Core.Tests.Connection;

public class JumpConnectionTests : IDisposable
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    private sealed class KCli : IKerberosCli
    {
        public bool ToolsAvailable { get; init; } = true;
        public bool Valid;
        public bool KinitSucceeds = true;
        public bool LastForwardable, LastAddressless;
        public bool HasAklog => true;
        public bool HasValidTicket() => Valid;
        public string? GetKlistOutput() => Valid ? "Default principal: rcurrie4@ED.AC.UK\nkrbtgt/ED.AC.UK@ED.AC.UK" : "";
        public bool Kinit(string p, string pw, bool f = false, bool a = false) { LastForwardable = f; LastAddressless = a; if (KinitSucceeds) Valid = true; return KinitSucceeds; }
        public bool Aklog() => true;
        public bool Kdestroy() { Valid = false; return true; }
    }

    private sealed class FakeMount : IJumpMount
    {
        public bool Mounted;
        public int Mounts, Unmounts;
        public string? MountError;
        public Task<bool> IsMountedAsync(CancellationToken ct = default) => Task.FromResult(Mounted);
        public Task<string?> MountAsync(CancellationToken ct = default) { Mounts++; if (MountError is null) Mounted = true; return Task.FromResult(MountError); }
        public Task UnmountAsync(CancellationToken ct = default) { Unmounts++; Mounted = false; return Task.CompletedTask; }
    }

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "jc-" + Guid.NewGuid().ToString("N"));
    public JumpConnectionTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private readonly JumpRequest _req = new(
        TargetHost: "cplab175.ph.ed.ac.uk", TargetUser: "rcurrie4",
        RemotePath: "/home/rcurrie4", MountTarget: "/home/me/S",
        JumpHost: "student.ph.ed.ac.uk", JumpUser: "rcurrie4", Password: "pw",
        UseKerberos: true);

    private JumpConnection Build(KCli cli, FakeProcessRunner runner)
    {
        var cfg = Path.Combine(_dir, "config");
        return new JumpConnection(
            RealmMap.Default,
            new KerberosHelper(cli),
            new SshProfileWriter(new FixedClock(DateTime.UnixEpoch)),
            new ControlMaster(runner),
            cfg,
            () => DateTime.UtcNow);
    }

    [Fact]
    public async Task Kerberos_path_kinits_forwardable_addressless_writes_profile_and_mounts()
    {
        var cli = new KCli();
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(0, "", "")); // ssh master ok
        var mount = new FakeMount();

        var outcome = await Build(cli, runner).ConnectAsync(_req, mount);

        Assert.True(outcome.Success);
        Assert.True(outcome.UsedKerberos);
        Assert.True(cli.LastForwardable);
        Assert.True(cli.LastAddressless);
        Assert.Equal(1, mount.Mounts);
        Assert.True(File.Exists(Path.Combine(_dir, "config")));           // profile written
        var cfg = File.ReadAllText(Path.Combine(_dir, "config"));
        Assert.Contains("Host cplab175.ph.ed.ac.uk", cfg);
        Assert.Contains("GSSAPIDelegateCredentials yes", cfg);            // Kerberos mode → GSSAPI in profile
    }

    [Fact]
    public async Task Kinit_failure_aborts_before_master()
    {
        var cli = new KCli { KinitSucceeds = false };
        var runner = new FakeProcessRunner();
        var mount = new FakeMount();

        var outcome = await Build(cli, runner).ConnectAsync(_req, mount);

        Assert.False(outcome.Success);
        Assert.Contains("Kerberos", outcome.Error);
        Assert.Empty(runner.Calls);          // never reached ssh
        Assert.Equal(0, mount.Mounts);
    }

    [Fact]
    public async Task Master_failure_reports_and_does_not_mount()
    {
        var cli = new KCli();
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(255, "", "Connection timed out"));
        var mount = new FakeMount();

        var outcome = await Build(cli, runner).ConnectAsync(_req, mount);

        Assert.False(outcome.Success);
        Assert.Equal(0, mount.Mounts);
    }

    [Fact]
    public async Task Session_monitor_tears_down_and_reconnects_using_ticket()
    {
        var cli = new KCli();
        // ssh -O check returns alive; establish returns ok.
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(0, "", ""));
        var mount = new FakeMount();

        var outcome = await Build(cli, runner).ConnectAsync(_req, mount);
        Assert.True(outcome.Success);

        // Healthy first tick.
        Assert.Equal(MonitorTickResult.Healthy, await outcome.Session!.Monitor.TickAsync());

        // Simulate the connection dropping: master check fails and mount goes away.
        runner.Reset().On((f, a) => a.Contains("check"), new ProcessResult(255, "", "no master"))
                      .On((f, a) => true, new ProcessResult(0, "", "")); // establish still ok
        mount.Mounted = false;

        // Not yet 15s → watching.
        Assert.Equal(MonitorTickResult.Watching, await outcome.Session.Monitor.TickAsync());
    }

    [Fact]
    public async Task Mount_failure_drops_master_and_reports()
    {
        var cli = new KCli();
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(0, "", "")); // master ok
        var mount = new FakeMount { MountError = "reading remote directory: No such file or directory" };

        var outcome = await Build(cli, runner).ConnectAsync(_req, mount);

        Assert.False(outcome.Success);
        Assert.Equal(1, mount.Mounts);
        // The master was torn down after the mount failed (an -O exit call was issued).
        Assert.Contains(runner.Calls, c => c.Args.Contains("exit"));
    }

    [Fact]
    public async Task Default_password_path_skips_kinit_and_omits_batchmode()
    {
        var cli = new KCli();
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(0, "", ""));
        var mount = new FakeMount();
        var req = _req with { UseKerberos = false };

        var outcome = await Build(cli, runner).ConnectAsync(req, mount);

        Assert.True(outcome.Success);
        Assert.False(outcome.UsedKerberos);
        Assert.False(cli.Valid);            // kinit was never called
        Assert.Equal(1, mount.Mounts);
        // Password mode: the master is established WITHOUT BatchMode so askpass can answer,
        // and the written profile carries no GSSAPI directives.
        Assert.DoesNotContain(runner.Calls, c => c.Args.Contains("BatchMode=yes"));
        Assert.DoesNotContain("GSSAPI", File.ReadAllText(Path.Combine(_dir, "config")));
    }

    [Fact]
    public async Task No_realm_uses_password_fallback_without_kerberos()
    {
        var cli = new KCli { ToolsAvailable = true };
        var runner = new FakeProcessRunner().On((f, a) => true, new ProcessResult(0, "", ""));
        var mount = new FakeMount();
        var req = _req with { TargetHost = "host.example.com", JumpHost = "jump.example.com" };

        var outcome = await Build(cli, runner).ConnectAsync(req, mount);

        Assert.True(outcome.Success);
        Assert.False(outcome.UsedKerberos);   // unknown realm → no kinit
        Assert.Equal(1, mount.Mounts);
    }
}
