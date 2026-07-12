using MountTool.Doctor;

namespace MountTool.Core.Tests.Doctor;

public class SshConfigParserTests
{
    [Fact]
    public void Parses_host_block_keywords_and_spans()
    {
        const string cfg = """
            # comment
            Host lxplus
                HostName lxplus.cern.ch
                User jbloggs
            """;
        var parsed = new SshConfigParser().ParseText(cfg);

        var block = parsed.Blocks.Single(b => b.Pattern == "lxplus");
        Assert.Equal(new[] { "HostName", "User" }, block.Entries.Select(e => e.Keyword));
        Assert.Equal("lxplus.cern.ch", block.Entries[0].Value);
        Assert.Equal(3, block.Entries[0].Span.StartLine); // 1=comment,2=Host,3=HostName
    }

    [Fact]
    public void Directives_before_any_host_form_a_global_block()
    {
        const string cfg = """
            ServerAliveInterval 30
            Host foo
                HostName foo.example
            """;
        var parsed = new SshConfigParser().ParseText(cfg);
        var global = parsed.Blocks.First();
        Assert.Equal("*", global.Pattern);
        Assert.Contains(global.Entries, e => e.Keyword == "ServerAliveInterval");
    }

    [Fact]
    public void Equals_syntax_is_parsed()
    {
        var (k, v) = SshConfigParser.SplitDirective("Port=2222");
        Assert.Equal("Port", k);
        Assert.Equal("2222", v);
    }

    [Fact]
    public void Include_pulls_in_another_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sshcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var extra = Path.Combine(dir, "extra.conf");
            File.WriteAllText(extra, "Host bastion\n    HostName bastion.example\n");
            var main = Path.Combine(dir, "config");
            File.WriteAllText(main, $"Include {extra}\nHost foo\n    HostName foo.example\n");

            var parsed = new SshConfigParser().Parse(main);
            Assert.Contains(parsed.Blocks, b => b.Pattern == "bastion");
            Assert.Contains(parsed.Blocks, b => b.Pattern == "foo");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
