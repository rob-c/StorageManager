using MountTool;
using MountTool.Doctor;

namespace MountTool.Core.Tests.Doctor;

public class ConfigFixerTests : IDisposable
{
    private sealed class FixedClock(DateTime t) : IClock { public DateTime UtcNow => t; }

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fixer-" + Guid.NewGuid().ToString("N"));

    public ConfigFixerTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string content)
    {
        var p = Path.Combine(_dir, "config");
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void SetOrReplace_replaces_line_and_preserves_comments()
    {
        var path = Write("# my config\nHost h\n    ServerAliveInterval 300\n    User bob\n");
        var fixer = new ConfigFixer(new FixedClock(new DateTime(2026, 7, 12, 9, 0, 0)));
        var fix = new SuggestedFix("x", "ServerAliveInterval", "30", "h", FixKind.SetOrReplace);

        var outcome = fixer.Apply(path, [fix], dryRun: false);

        var result = File.ReadAllText(path);
        Assert.Contains("# my config", result);
        Assert.Contains("ServerAliveInterval 30", result);
        Assert.DoesNotContain("ServerAliveInterval 300", result);
        Assert.Contains("User bob", result);
        Assert.True(outcome.Applied);
        Assert.NotNull(outcome.BackupPath);
        Assert.True(File.Exists(outcome.BackupPath!));
    }

    [Fact]
    public void SetOrReplace_appends_when_absent()
    {
        var path = Write("Host h\n    User bob\n");
        var fixer = new ConfigFixer(new FixedClock(DateTime.UnixEpoch));
        var fix = new SuggestedFix("x", "ServerAliveInterval", "30", "h", FixKind.SetOrReplace);

        fixer.Apply(path, [fix], dryRun: false);

        var parsed = new SshConfigParser().Parse(path);
        var eff = new EffectiveConfigResolver().Resolve(parsed, "h");
        Assert.Equal("30", eff.Values["ServerAliveInterval"]);
    }

    [Fact]
    public void DryRun_writes_nothing_and_returns_diff()
    {
        var original = "Host h\n    Compression yes\n";
        var path = Write(original);
        var fixer = new ConfigFixer(new FixedClock(DateTime.UnixEpoch));
        var fix = new SuggestedFix("x", "Compression", "no", "h", FixKind.SetOrReplace);

        var outcome = fixer.Apply(path, [fix], dryRun: true);

        Assert.Equal(original, File.ReadAllText(path)); // untouched
        Assert.Null(outcome.BackupPath);
        Assert.False(outcome.Applied);
        Assert.Contains("-    Compression yes", outcome.UnifiedDiff);
        Assert.Contains("+    Compression no", outcome.UnifiedDiff);
    }

    [Fact]
    public void Apply_is_idempotent()
    {
        var path = Write("Host h\n    Compression yes\n");
        var fixer = new ConfigFixer(new FixedClock(DateTime.UnixEpoch));
        var fix = new SuggestedFix("x", "Compression", "no", "h", FixKind.SetOrReplace);

        fixer.Apply(path, [fix], dryRun: false);
        var afterFirst = File.ReadAllText(path);
        var second = fixer.Apply(path, [fix], dryRun: false);

        Assert.False(second.Applied); // no change the second time
        Assert.Equal(afterFirst, File.ReadAllText(path));
    }
}
