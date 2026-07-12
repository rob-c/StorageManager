using StorageManager;
using StorageManager.Mounting;

namespace StorageManager.Core.Tests;

public class ReadOnlyArgumentTests
{
    private static List<string> ArgsFor(bool readOnly)
    {
        var config = Config.Default with
        {
            Gateway = "host.example",
            RemotePath = "/home/x",
            MountTarget = "/tmp/mnt",
            ReadOnly = readOnly,
        };
        return new LinuxMounter(config).BuildArguments("jbloggs");
    }

    [Fact]
    public void ReadOnly_is_the_default()
    {
        Assert.True(Config.Default.ReadOnly);
    }

    [Fact]
    public void ReadOnly_mount_passes_o_ro()
    {
        var args = ArgsFor(readOnly: true);
        var i = args.IndexOf("ro");
        Assert.True(i > 0 && args[i - 1] == "-o", "expected an '-o ro' pair");
    }

    [Fact]
    public void ReadWrite_mount_omits_ro()
    {
        Assert.DoesNotContain("ro", ArgsFor(readOnly: false));
    }
}

public class JumpArgumentTests
{
    private static List<string> ArgsFor(string? jump, bool gssapi = false)
    {
        var config = Config.Default with
        {
            Gateway = "cplab175.ph.ed.ac.uk",
            RemotePath = "/home/x",
            MountTarget = "/tmp/mnt",
            JumpHost = jump,
            UseGssapi = gssapi,
        };
        return new LinuxMounter(config).BuildArguments("rcurrie4");
    }

    [Fact]
    public void Jump_mount_adds_proxyjump_and_uses_askpass_not_stdin()
    {
        var args = ArgsFor("student.ph.ed.ac.uk");
        Assert.Contains("ProxyJump=student.ph.ed.ac.uk", args);
        Assert.DoesNotContain("password_stdin", args);              // askpass answers both hops
        Assert.Contains("PreferredAuthentications=password", args);
        Assert.Contains("NumberOfPasswordPrompts=4", args);         // one per hop, with retries
        Assert.Contains("ConnectTimeout=45", args);                 // double hop needs longer
    }

    [Fact]
    public void Jump_with_gssapi_prefers_kerberos_with_password_fallback()
    {
        var args = ArgsFor("student.ph.ed.ac.uk", gssapi: true);
        Assert.Contains("GSSAPIAuthentication=yes", args);
        Assert.Contains("GSSAPIDelegateCredentials=yes", args);
        // No PreferredAuthentications: ssh's default order tries GSSAPI, then
        // password (comma-separated -o values are unsafe under Linux FUSE).
        Assert.DoesNotContain(args, a => a.StartsWith("PreferredAuthentications="));
    }

    [Fact]
    public void Direct_mount_unchanged()
    {
        var args = ArgsFor(jump: null);
        Assert.DoesNotContain(args, a => a.StartsWith("ProxyJump="));
        Assert.Contains("password_stdin", args);
        Assert.Contains("NumberOfPasswordPrompts=1", args);
    }

    [Fact]
    public void Windows_jump_uses_explicit_proxycommand_not_proxyjump()
    {
        var config = Config.Default with
        {
            Gateway = "cplab175.ph.ed.ac.uk", RemotePath = "/home/x", MountTarget = "S:",
            JumpHost = "student.ph.ed.ac.uk",
        };
        var args = new WindowsMounter(config).BuildArguments("rcurrie4");

        Assert.DoesNotContain(args, a => a.StartsWith("ProxyJump="));
        var proxy = Assert.Single(args, a => a.StartsWith("ProxyCommand="));
        // Absolute, forward-slashed, single-quoted ssh.exe path so busybox can run it.
        Assert.Contains("'C:/Program Files/SSHFS-Win/bin/ssh.exe'", proxy);
        Assert.Contains("-W \"[%h]:%p\" student.ph.ed.ac.uk", proxy);   // quoted like OpenSSH's own
        Assert.Contains("-l rcurrie4", proxy);
        // Password-only on the jump too (no GSSAPI) when Kerberos is off.
        Assert.Contains("GSSAPIAuthentication=no", proxy);
        Assert.Contains("PreferredAuthentications=password", proxy);
        Assert.DoesNotContain("/usr/bin/ssh ", proxy);   // the broken cygwin-path form
    }
}
