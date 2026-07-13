using StorageManager;

namespace StorageManager.Core.Tests;

public class HostPathTests
{
    private static IReadOnlyList<string> PathsFor(string host) =>
        Config.Default.HostList.Single(h => h.Name == host).RemotePaths!;

    [Fact]
    public void Staff_exposes_datastore_group_and_personal_roots_but_not_ppegroup()
    {
        var paths = PathsFor("staff.ph.ed.ac.uk");
        Assert.Contains("/storage/datastore-group", paths);
        Assert.Contains("/storage/datastore-personal", paths);
        Assert.DoesNotContain("/mnt/gridpp/poolhomes/PPEGroup", paths);
    }

    [Theory]
    [InlineData("t3-mw2.ph.ed.ac.uk")]
    [InlineData("phcomputeppe01.ph.ed.ac.uk")]
    public void Batch_hosts_expose_ppegroup(string host)
    {
        Assert.Contains("/mnt/gridpp/poolhomes/PPEGroup", PathsFor(host));
    }

    [Fact]
    public void Ppegroup_is_restricted_to_the_batch_hosts()
    {
        var withPpeGroup = Config.Default.HostList
            .Where(h => h.RemotePaths?.Contains("/mnt/gridpp/poolhomes/PPEGroup") == true)
            .Select(h => h.Name)
            .OrderBy(n => n);
        Assert.Equal(["phcomputeppe01.ph.ed.ac.uk", "t3-mw2.ph.ed.ac.uk"], withPpeGroup);
    }
}
