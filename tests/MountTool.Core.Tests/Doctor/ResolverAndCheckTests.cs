using MountTool.Doctor;
using MountTool.Doctor.Checks;

namespace MountTool.Core.Tests.Doctor;

public class ResolverAndCheckTests
{
    private static DoctorContext Context(string cfg, string host, params ProbeOutcome[] probes)
    {
        var parsed = new SshConfigParser().ParseText(cfg);
        var effective = new EffectiveConfigResolver().Resolve(parsed, host);
        return new DoctorContext(parsed, effective, probes);
    }

    [Fact]
    public void Resolver_first_value_wins_and_wildcard_applies_last()
    {
        const string cfg = """
            Host lxplus
                Compression no
            Host *
                Compression yes
                ServerAliveInterval 30
            """;
        var eff = new EffectiveConfigResolver().Resolve(new SshConfigParser().ParseText(cfg), "lxplus");
        Assert.Equal("no", eff.Values["Compression"]);          // specific block wins
        Assert.Equal("30", eff.Values["ServerAliveInterval"]);  // inherited from wildcard
    }

    [Fact]
    public void Resolver_builds_jump_chain()
    {
        const string cfg = """
            Host target
                ProxyJump bastion
            """;
        var eff = new EffectiveConfigResolver().Resolve(new SshConfigParser().ParseText(cfg), "target");
        Assert.Equal(new[] { "bastion" }, eff.JumpChain);
    }

    [Fact]
    public void Keepalive_unset_yields_warning_with_fix()
    {
        var ctx = Context("Host h\n    HostName h.example\n", "h");
        var f = new KeepaliveCheck().Run(ctx).Single();
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.NotNull(f.Fix);
        Assert.Equal("ServerAliveInterval", f.Fix!.Keyword);
        Assert.Equal("30", f.Fix.NewValue);
    }

    [Fact]
    public void Keepalive_tunes_to_observed_idle_reset()
    {
        var probe = new ProbeOutcome("h", 22, true, true, TimeSpan.FromSeconds(90));
        var ctx = Context("Host h\n    HostName h.example\n", "h", probe);
        var f = new KeepaliveCheck().Run(ctx).Single();
        Assert.Equal("45", f.Fix!.NewValue);
    }

    [Fact]
    public void Dpi_flags_compression_and_ipqos()
    {
        var ctx = Context("Host h\n    Compression yes\n    IPQoS lowdelay throughput\n", "h");
        var findings = new DpiResilienceCheck().Run(ctx).ToList();
        Assert.Contains(findings, f => f.Fix!.Keyword == "Compression" && f.Fix.NewValue == "no");
        Assert.Contains(findings, f => f.Fix!.Keyword == "IPQoS" && f.Fix.NewValue == "none");
    }

    [Fact]
    public void Jumphost_unconfigured_hop_is_error()
    {
        var ctx = Context("Host target\n    ProxyJump missingbastion\n", "target");
        var f = new JumpHostCheck().Run(ctx).Single();
        Assert.Equal(Severity.Error, f.Severity);
    }

    [Fact]
    public void Jumphost_with_defined_block_is_ok()
    {
        const string cfg = """
            Host target
                ProxyJump bastion
            Host bastion
                HostName bastion.example
            """;
        var ctx = Context(cfg, "target");
        Assert.Empty(new JumpHostCheck().Run(ctx));
    }

    [Fact]
    public void Footgun_flags_stricthostkey_no_and_devnull()
    {
        const string cfg = """
            Host h
                StrictHostKeyChecking no
                UserKnownHostsFile /dev/null
            """;
        var ctx = Context(cfg, "h");
        var findings = new FootgunCheck().Run(ctx).ToList();
        Assert.Contains(findings, f => f.Title.Contains("StrictHostKeyChecking"));
        Assert.Contains(findings, f => f.Title.Contains("/dev/null"));
    }

    [Fact]
    public void Footgun_flags_typo_keyword()
    {
        var ctx = Context("Host h\n    Hostnme h.example\n", "h");
        var f = new FootgunCheck().Run(ctx).Single(x => x.Title.Contains("Unknown keyword"));
        Assert.Contains("HostName", f.Explanation);
    }

    [Fact]
    public void Footgun_flags_duplicate_host_blocks()
    {
        const string cfg = """
            Host dup
                HostName a
            Host dup
                User b
            """;
        var ctx = Context(cfg, "dup");
        Assert.Contains(new FootgunCheck().Run(ctx), f => f.Title.Contains("Duplicate"));
    }
}
