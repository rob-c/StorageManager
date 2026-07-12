using StorageManager;

namespace StorageManager.Core.Tests.Connection;

public class ConfigJumpTests
{
    [Fact]
    public void Default_offers_edinburgh_jump_hosts()
    {
        Assert.Contains("student.ph.ed.ac.uk", Config.Default.JumpHostList);
        Assert.Contains("staff.ph.ed.ac.uk", Config.Default.JumpHostList);
    }

    [Fact]
    public void Realm_overrides_from_config_are_applied()
    {
        var cfg = Config.Default with
        {
            KerberosRealms = new Dictionary<string, string> { ["desy.de"] = "DESY.DE" },
        };
        var map = cfg.BuildRealmMap();
        Assert.Equal("DESY.DE", map.RealmFor("naf.desy.de"));
        Assert.Equal("FNAL.GOV", map.RealmFor("dunegpvm01.fnal.gov")); // defaults preserved
    }

    [Fact]
    public void Fermilab_host_entry_present_with_nashome_and_pnfs()
    {
        var fnal = Config.Default.HostList.Single(h => h.Name.EndsWith("fnal.gov"));
        Assert.False(fnal.TwoFactorPam);
        Assert.Contains("/nashome/$USER1/$USER", fnal.RemotePaths!);
        Assert.Contains(fnal.RemotePaths!, p => p.StartsWith("/pnfs/"));
        Assert.DoesNotContain(fnal.RemotePaths!, p => p.StartsWith("/eos/"));
    }
}
