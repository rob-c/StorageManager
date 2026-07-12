using StorageManager.Auth;
using StorageManager.Ssh;

namespace StorageManager.Core.Tests.Ssh;

public class RealmMapTests
{
    [Theory]
    [InlineData("cplab175.ph.ed.ac.uk", "ED.AC.UK")]
    [InlineData("student.ph.ed.ac.uk", "ED.AC.UK")]
    [InlineData("staff.ph.ed.ac.uk", "ED.AC.UK")]
    [InlineData("lxplus.cern.ch", "CERN.CH")]
    [InlineData("eosuser.cern.ch", "CERN.CH")]
    [InlineData("dunegpvm01.fnal.gov", "FNAL.GOV")]
    public void Maps_known_domains(string host, string realm)
    {
        Assert.Equal(realm, RealmMap.Default.RealmFor(host));
    }

    [Fact]
    public void Unknown_domain_returns_null()
    {
        Assert.Null(RealmMap.Default.RealmFor("example.com"));
        Assert.Null(RealmMap.Default.RealmFor(""));
    }

    [Fact]
    public void Overrides_add_and_win()
    {
        var map = new RealmMap(new Dictionary<string, string> { ["desy.de"] = "DESY.DE" });
        Assert.Equal("DESY.DE", map.RealmFor("naf.desy.de"));
        Assert.Equal("ED.AC.UK", map.RealmFor("cplab175.ph.ed.ac.uk")); // defaults still present
    }

    [Fact]
    public void Principal_combines_user_and_realm()
    {
        Assert.Equal("rcurrie4@ED.AC.UK", RealmMap.Default.PrincipalFor("rcurrie4", "cplab175.ph.ed.ac.uk"));
        Assert.Null(RealmMap.Default.PrincipalFor("rcurrie4", "example.com"));
    }
}

/// <summary>A scripted process runner for tests: matches on the command and returns canned results.</summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    private readonly List<(Func<string, IReadOnlyList<string>, bool> Match, Func<ProcessResult> Result)> _rules = new();
    public List<(string File, IReadOnlyList<string> Args, string? Stdin)> Calls { get; } = new();

    public FakeProcessRunner On(Func<string, IReadOnlyList<string>, bool> match, ProcessResult result)
    {
        _rules.Add((match, () => result));
        return this;
    }

    public FakeProcessRunner On(Func<string, IReadOnlyList<string>, bool> match, Func<ProcessResult> result)
    {
        _rules.Add((match, result));
        return this;
    }

    public Task<ProcessResult> RunAsync(
        string file, IReadOnlyList<string> args, string? stdin = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        Calls.Add((file, args, stdin));
        foreach (var (match, result) in _rules)
            if (match(file, args))
                return Task.FromResult(result());
        return Task.FromResult(new ProcessResult(0, "", ""));
    }
}
