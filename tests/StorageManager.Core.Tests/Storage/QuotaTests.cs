using StorageManager.Storage;

namespace StorageManager.Core.Tests.Storage;

public class QuotaParsersTests
{
    [Fact]
    public void ParseDf_reads_used_and_total_in_bytes()
    {
        const string df = """
            Filesystem     1024-blocks    Used Available Capacity Mounted on
            host:/export     104857600 26214400  78643200      25% /home/x
            """;
        var q = QuotaParsers.ParseDf(df, "/home/x");
        Assert.NotNull(q);
        Assert.Equal(26214400L * 1024, q!.UsedBytes);
        Assert.Equal(104857600L * 1024, q.LimitBytes);
        Assert.Equal(25, q.Percent!.Value, 0);
    }

    [Fact]
    public void ParseAfsListQuota_reads_kib()
    {
        const string fs = """
            Volume Name           Quota      Used  %Used   Partition
            user.jbloggs        5000000   1250000    25%       48%
            """;
        var q = QuotaParsers.ParseAfsListQuota(fs, "/afs/cern.ch/user/j/jbloggs");
        Assert.NotNull(q);
        Assert.Equal(1250000L * 1024, q!.UsedBytes);
        Assert.Equal(5000000L * 1024, q.LimitBytes);
    }

    [Fact]
    public void ParseEosMonitoring_reads_used_and_max()
    {
        const string eos = "quota=node uid=jbloggs space=/eos/user/ usedbytes=2147483648 maxbytes=5368709120 usedfiles=100";
        var q = QuotaParsers.ParseEosQuotaMonitoring(eos, "/eos/user/j/jbloggs");
        Assert.NotNull(q);
        Assert.Equal(2147483648L, q!.UsedBytes);
        Assert.Equal(5368709120L, q.LimitBytes);
    }

    [Fact]
    public void ParseEosMonitoring_treats_zero_max_as_unlimited()
    {
        var q = QuotaParsers.ParseEosQuotaMonitoring("usedbytes=100 maxbytes=0", "/eos/x");
        Assert.NotNull(q);
        Assert.Null(q!.LimitBytes);
        Assert.Null(q.Percent);
    }

    [Fact]
    public void ParseQuotaDashS_reads_human_sizes()
    {
        const string quota = """
            Disk quotas for user jbloggs (uid 1000):
                 Filesystem   space   quota   limit   grace
                 /dev/sda1     12.0G     20G     25G
            """;
        var q = QuotaParsers.ParseQuotaDashS(quota, "/home/jbloggs");
        Assert.NotNull(q);
        Assert.Equal((long)(12.0 * 1024 * 1024 * 1024), q!.UsedBytes);
        Assert.Equal(20L * 1024 * 1024 * 1024, q.LimitBytes);
    }

    [Fact]
    public void ParseDf_returns_null_on_garbage()
    {
        Assert.Null(QuotaParsers.ParseDf("nonsense", "/x"));
    }
}

public class QuotaProbeTests
{
    private sealed class FakeExec(Func<string, RemoteResult> handler) : IRemoteExec
    {
        public Task<RemoteResult> RunAsync(string host, string user, string command, CancellationToken ct = default)
            => Task.FromResult(handler(command));
    }

    [Fact]
    public async Task Uses_fs_listquota_for_afs_paths()
    {
        var exec = new FakeExec(cmd => cmd.StartsWith("fs listquota")
            ? new RemoteResult(0, "Volume Name Quota Used\nuser.j 5000000 1000000 20%", "")
            : new RemoteResult(1, "", "not called"));

        var quotas = await new QuotaProbe(exec).GatherRemoteAsync(
            "lxplus.cern.ch", "jbloggs", ["/afs/cern.ch/user/j/jbloggs"]);

        Assert.Single(quotas);
        Assert.Equal("AFS", quotas[0].Label);
        Assert.Equal(1000000L * 1024, quotas[0].UsedBytes);
    }

    [Fact]
    public async Task Uses_eos_quota_for_eos_paths()
    {
        var exec = new FakeExec(cmd => cmd.StartsWith("eos quota")
            ? new RemoteResult(0, "usedbytes=1073741824 maxbytes=2147483648", "")
            : new RemoteResult(1, "", ""));

        var quotas = await new QuotaProbe(exec).GatherRemoteAsync(
            "lxplus.cern.ch", "jbloggs", ["/eos/user/j/jbloggs"]);

        Assert.Single(quotas);
        Assert.Equal("EOS", quotas[0].Label);
    }

    [Fact]
    public async Task Falls_back_to_df_when_specific_command_fails()
    {
        var exec = new FakeExec(cmd => cmd.StartsWith("df -P")
            ? new RemoteResult(0, "FS 1024-blocks Used Avail Cap Mount\nx 1000 400 600 40% /eos/x", "")
            : new RemoteResult(1, "", "unavailable"));

        var quotas = await new QuotaProbe(exec).GatherRemoteAsync(
            "lxplus.cern.ch", "jbloggs", ["/eos/user/j/jbloggs"]);

        Assert.Single(quotas);
        Assert.Equal("Filesystem", quotas[0].Label);
    }
}
