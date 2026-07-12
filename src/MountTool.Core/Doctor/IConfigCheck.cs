namespace MountTool.Doctor;

/// <summary>Everything a check needs: the parsed config, the effective view, and any probe results.</summary>
public sealed record DoctorContext(
    SshConfig Config,
    EffectiveConfig Effective,
    IReadOnlyList<ProbeOutcome> Probes);

/// <summary>A single, independent audit rule over an ssh_config.</summary>
public interface IConfigCheck
{
    IEnumerable<Finding> Run(DoctorContext ctx);
}

/// <summary>The aggregated result of a doctor run.</summary>
public sealed record DoctorReport(
    string Host,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<ProbeOutcome> Probes)
{
    public bool HasErrors => Findings.Any(f => f.Severity == Severity.Error);
    public bool HasFindings => Findings.Count > 0;
}
