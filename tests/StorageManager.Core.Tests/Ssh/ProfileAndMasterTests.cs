using StorageManager;
using StorageManager.Doctor;
using StorageManager.Ssh;

namespace StorageManager.Core.Tests.Ssh;

public class SshProfileWriterTests : IDisposable
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "profile-" + Guid.NewGuid().ToString("N"));

    public SshProfileWriterTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private readonly JumpProfile _profile = new(
        TargetHost: "cplab175.ph.ed.ac.uk", TargetUser: "rcurrie4",
        JumpHost: "student.ph.ed.ac.uk", JumpUser: "rcurrie4");

    [Fact]
    public void Writes_target_and_jump_blocks_with_expected_effective_values()
    {
        var path = Path.Combine(_dir, "config");
        var writer = new SshProfileWriter(new FixedClock(DateTime.UnixEpoch));

        var outcome = writer.Apply(path, _profile);
        Assert.True(outcome.Applied);
        Assert.NotNull(outcome.BackupPath is null ? "new file ok" : outcome.BackupPath); // new file → no backup

        var cfg = new SshConfigParser().Parse(path);
        var resolver = new EffectiveConfigResolver();

        var target = resolver.Resolve(cfg, "cplab175.ph.ed.ac.uk").Values;
        Assert.Equal("student.ph.ed.ac.uk", target["ProxyJump"]);
        Assert.Equal("yes", target["GSSAPIDelegateCredentials"]);
        Assert.Equal("auto", target["ControlMaster"]);
        Assert.Equal("~/.ssh/cm/%C", target["ControlPath"]);
        Assert.Equal("30s", target["ControlPersist"]);
        Assert.Equal("5", target["ServerAliveInterval"]);

        var jump = resolver.Resolve(cfg, "student.ph.ed.ac.uk").Values;
        Assert.Equal("student.ph.ed.ac.uk", jump["HostName"]);
        Assert.Equal("yes", jump["GSSAPIAuthentication"]);
        Assert.False(jump.ContainsKey("ProxyJump")); // jump has no jump
        // The TGT is delegated only to the target, never to the intermediate jump.
        Assert.Equal("yes", target["GSSAPIDelegateCredentials"]);
        Assert.False(jump.ContainsKey("GSSAPIDelegateCredentials"));
    }

    [Fact]
    public void Creates_control_socket_directory()
    {
        var path = Path.Combine(_dir, "config");
        new SshProfileWriter(new FixedClock(DateTime.UnixEpoch)).Apply(path, _profile);
        // Default ControlPath is ~/.ssh/cm/%C — the cm dir must exist afterwards.
        var cm = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "cm");
        Assert.True(Directory.Exists(cm));
    }

    [Theory]
    [InlineData("host\n    ProxyCommand evil", "rcurrie4")]
    [InlineData("good.host", "bad user")]
    [InlineData("has space", "rcurrie4")]
    public void Rejects_injection_in_host_or_user(string target, string user)
    {
        var bad = _profile with { TargetHost = target, TargetUser = user };
        Assert.Throws<ArgumentException>(() =>
            new SshProfileWriter(new FixedClock(DateTime.UnixEpoch)).Apply(Path.Combine(_dir, "config"), bad));
    }

    [Fact]
    public void Is_idempotent()
    {
        var path = Path.Combine(_dir, "config");
        var writer = new SshProfileWriter(new FixedClock(DateTime.UnixEpoch));
        writer.Apply(path, _profile);
        var first = File.ReadAllText(path);
        var second = writer.Apply(path, _profile);
        Assert.False(second.Applied);          // nothing to change the second time
        Assert.Equal(first, File.ReadAllText(path));
    }

    [Fact]
    public void Preserves_existing_unrelated_config()
    {
        var path = Path.Combine(_dir, "config");
        File.WriteAllText(path, "# my stuff\nHost myserver\n    HostName my.example\n");
        new SshProfileWriter(new FixedClock(DateTime.UnixEpoch)).Apply(path, _profile);
        var text = File.ReadAllText(path);
        Assert.Contains("# my stuff", text);
        Assert.Contains("Host myserver", text);
        Assert.Contains("Host cplab175.ph.ed.ac.uk", text);
    }
}

public class ControlMasterTests
{
    [Fact]
    public async Task Establish_issues_master_flags()
    {
        var runner = new FakeProcessRunner();
        await new ControlMaster(runner).EstablishAsync("cplab175.ph.ed.ac.uk");
        var (file, args, _) = runner.Calls.Single();
        Assert.Equal("ssh", file);
        Assert.Contains("-M", args);
        Assert.Contains("-N", args);
        Assert.Contains("-f", args);
        Assert.Equal("cplab175.ph.ed.ac.uk", args[^1]);
    }

    [Fact]
    public async Task IsAlive_maps_check_exit_code()
    {
        var alive = new FakeProcessRunner().On((f, a) => a.Contains("check"), new ProcessResult(0, "", ""));
        var dead = new FakeProcessRunner().On((f, a) => a.Contains("check"), new ProcessResult(255, "", "no master"));
        Assert.True(await new ControlMaster(alive).IsAliveAsync("h"));
        Assert.False(await new ControlMaster(dead).IsAliveAsync("h"));
    }

    [Fact]
    public async Task Establish_without_batchmode_omits_the_flag()
    {
        var runner = new FakeProcessRunner();
        await new ControlMaster(runner).EstablishAsync("h", batchMode: false);
        var (_, args, _) = runner.Calls.Single();
        Assert.DoesNotContain("BatchMode=yes", args);
        Assert.Contains("-M", args);
    }

    [Fact]
    public async Task Exit_issues_O_exit()
    {
        var runner = new FakeProcessRunner();
        await new ControlMaster(runner).ExitAsync("cplab175.ph.ed.ac.uk");
        var (_, args, _) = runner.Calls.Single();
        Assert.Contains("-O", args);
        Assert.Contains("exit", args);
    }
}
