namespace MountTool.Doctor;

/// <summary>Result of an active network probe against a host/port.</summary>
public sealed record ProbeOutcome(
    string Host,
    int Port,
    bool Reachable,
    bool BannerSeen,
    TimeSpan? IdleResetAfter);

/// <summary>Actively probes a host: TCP reach, SSH banner, and optional idle-reset timing.</summary>
public interface IDoctorProbe
{
    Task<ProbeOutcome> ProbeAsync(string host, int port, bool idleTest, CancellationToken ct);
}
