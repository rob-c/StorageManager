namespace StorageManager.Doctor;

/// <summary>
/// Orchestrates a doctor run: parse the config, resolve the effective view for a
/// host, optionally run active probes, then execute every check and aggregate
/// the findings (most severe first).
/// </summary>
public sealed class SshDoctor
{
    private readonly IReadOnlyList<IConfigCheck> _checks;
    private readonly IDoctorProbe _probe;
    private readonly SshConfigParser _parser = new();
    private readonly EffectiveConfigResolver _resolver = new();

    public SshDoctor(IEnumerable<IConfigCheck> checks, IDoctorProbe probe)
    {
        _checks = checks.ToList();
        _probe = probe;
    }

    /// <summary>The standard check set plus a real probe.</summary>
    public static SshDoctor CreateDefault() => new(
        [
            new Checks.KeepaliveCheck(),
            new Checks.DpiResilienceCheck(),
            new Checks.JumpHostCheck(),
            new Checks.FootgunCheck(),
        ],
        new DoctorProbe());

    public async Task<DoctorReport> RunAsync(
        string configPath, string host, bool runProbes, bool idleTest = false, CancellationToken ct = default)
    {
        var config = File.Exists(configPath) ? _parser.Parse(configPath) : new SshConfig([], []);
        var effective = _resolver.Resolve(config, host);

        var probes = new List<ProbeOutcome>();
        if (runProbes)
        {
            var target = effective.Values.GetValueOrDefault("HostName", host);
            probes.Add(await _probe.ProbeAsync(target, 22, idleTest, ct));
            probes.Add(await _probe.ProbeAsync(target, 443, idleTest: false, ct));
        }

        var ctx = new DoctorContext(config, effective, probes);
        var findings = _checks
            .SelectMany(c => SafeRun(c, ctx))
            .OrderByDescending(f => f.Severity)
            .ToList();

        return new DoctorReport(host, findings, probes);
    }

    private static IEnumerable<Finding> SafeRun(IConfigCheck check, DoctorContext ctx)
    {
        try { return check.Run(ctx).ToList(); }
        catch { return []; }
    }
}
