using MountTool;

namespace MountTool.Core.Tests;

public class ConfigDefaultTests
{
    [Fact]
    public void Default_lists_expected_hosts()
    {
        var names = Config.Default.HostList.Select(h => h.Name).ToArray();
        Assert.Contains("staff.ph.ed.ac.uk", names);
        Assert.Contains("lxplus.cern.ch", names);
    }

    [Fact]
    public void Lxplus_offers_afs_work_eos_home_and_experiment_roots()
    {
        var lxplus = Config.Default.HostList.Single(h => h.Name == "lxplus.cern.ch");
        Assert.Equal(new[]
        {
            "/afs/cern.ch/user/$USER1/$USER",
            "/afs/cern.ch/work/$USER1/$USER",
            "/eos/user/$USER1/$USER",
            "/eos/home-$USER1/$USER",
            "/eos/experiment/atlas",
            "/eos/experiment/cms",
            "/eos/experiment/lhcb",
            "/eos/experiment/alice",
        }, lxplus.RemotePaths);
    }
}
