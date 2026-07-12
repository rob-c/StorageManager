using MountTool.Auth;

namespace MountTool.Core.Tests.Auth;

public class KlistParserTests
{
    [Fact]
    public void Parses_mit_klist_output()
    {
        const string mit = """
            Ticket cache: FILE:/tmp/krb5cc_1000
            Default principal: jbloggs@CERN.CH

            Valid starting     Expires            Service principal
            07/12/26 09:00:00  07/12/26 19:00:00  krbtgt/CERN.CH@CERN.CH
            """;
        var (principal, realm, detail) = KlistParser.Parse(mit);
        Assert.Equal("jbloggs@CERN.CH", principal);
        Assert.Equal("CERN.CH", realm);
        Assert.Contains("krbtgt/CERN.CH", detail);
    }

    [Fact]
    public void Parses_heimdal_klist_output()
    {
        const string heimdal = """
            Credentials cache: FILE:/tmp/krb5cc_1000
                    Principal: jbloggs@CERN.CH

              Issued                Expires               Principal
            Jul 12 09:00:00 2026  Jul 12 19:00:00 2026  krbtgt/CERN.CH@CERN.CH
            """;
        var (principal, realm, _) = KlistParser.Parse(heimdal);
        Assert.Equal("jbloggs@CERN.CH", principal);
        Assert.Equal("CERN.CH", realm);
    }
}

public class KerberosHelperTests
{
    private sealed class FakeCli : IKerberosCli
    {
        public bool ToolsAvailable { get; init; } = true;
        public bool HasAklog { get; init; } = true;
        public bool Valid;
        public string Principal = "jbloggs@CERN.CH";
        public bool AklogCalled;
        public bool KinitShouldSucceed = true;

        public bool HasValidTicket() => Valid;
        public string? GetKlistOutput() => Valid ? $"Default principal: {Principal}\nkrbtgt/CERN.CH@CERN.CH" : "";
        public bool Kinit(string principal, string password) { if (KinitShouldSucceed) Valid = true; return KinitShouldSucceed; }
        public bool Aklog() { AklogCalled = true; return true; }
        public bool Kdestroy() { Valid = false; return true; }
    }

    [Fact]
    public void Status_reports_no_tools_when_unavailable()
    {
        var helper = new KerberosHelper(new FakeCli { ToolsAvailable = false });
        Assert.False(helper.Status().ToolsAvailable);
    }

    [Fact]
    public void Authenticate_succeeds_and_runs_aklog()
    {
        var cli = new FakeCli();
        var status = new KerberosHelper(cli).Authenticate("jbloggs@CERN.CH", "pw");
        Assert.True(status.HasValidTicket);
        Assert.Equal("CERN.CH", status.Realm);
        Assert.True(cli.AklogCalled);
    }

    [Fact]
    public void Authenticate_failure_reports_no_ticket()
    {
        var cli = new FakeCli { KinitShouldSucceed = false };
        var status = new KerberosHelper(cli).Authenticate("jbloggs@CERN.CH", "wrong");
        Assert.False(status.HasValidTicket); // verified via cache, not kinit's word
        Assert.False(cli.AklogCalled);
    }

    [Fact]
    public void Destroy_clears_ticket()
    {
        var cli = new FakeCli { Valid = true };
        Assert.False(new KerberosHelper(cli).Destroy().HasValidTicket);
    }
}
