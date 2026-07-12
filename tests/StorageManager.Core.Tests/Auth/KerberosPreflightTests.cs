using StorageManager.Auth;
using StorageManager.Errors;

namespace StorageManager.Core.Tests.Auth;

public class KerberosPreflightTests
{
    private sealed class Cli(bool available) : IKerberosCli
    {
        public bool ToolsAvailable => available;
        public bool HasAklog => false;
        public bool HasValidTicket() => false;
        public string? GetKlistOutput() => null;
        public bool Kinit(string principal, string password, bool forwardable = false, bool addressless = false) => false;
        public bool Aklog() => false;
        public bool Kdestroy() => false;
    }

    [Fact]
    public void Available_tools_pass()
    {
        Assert.Null(KerberosPreflight.Check(new Cli(true), isWindows: true));
    }

    [Fact]
    public void Windows_missing_offers_winget_mit_kerberos()
    {
        var r = KerberosPreflight.Check(new Cli(false), isWindows: true);
        Assert.NotNull(r);
        Assert.Equal(FixKindUi.WingetInstall, r!.Fix!.Kind);
        Assert.Equal("MIT.Kerberos", r.Fix.Payload);
    }

    [Fact]
    public void Unix_missing_offers_copy_command()
    {
        var r = KerberosPreflight.Check(new Cli(false), isWindows: false);
        Assert.NotNull(r);
        Assert.Equal(FixKindUi.CopyCommand, r!.Fix!.Kind);
        Assert.Contains("krb5", r.Fix.Payload);
    }
}
