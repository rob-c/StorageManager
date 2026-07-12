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
