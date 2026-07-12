using StorageManager.Doctor;
using StorageManager.Doctor.Checks;

namespace StorageManager.Core.Tests.Doctor;

public class SshDoctorTests : IDisposable
{
    private sealed class FakeProbe(ProbeOutcome outcome) : IDoctorProbe
    {
        public Task<ProbeOutcome> ProbeAsync(string host, int port, bool idleTest, CancellationToken ct)
            => Task.FromResult(outcome with { Host = host, Port = port });
    }

    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "doctor-" + Guid.NewGuid().ToString("N") + ".config");

    public void Dispose() { try { File.Delete(_path); } catch { } }

    [Fact]
    public async Task Reports_footgun_from_fixture()
    {
        File.WriteAllText(_path, "Host h\n    StrictHostKeyChecking no\n");
        var doctor = new SshDoctor(
            [new FootgunCheck(), new KeepaliveCheck()],
            new FakeProbe(new ProbeOutcome("h", 22, true, true, null)));

        var report = await doctor.RunAsync(_path, "h", runProbes: false);

        Assert.Contains(report.Findings, f => f.Title.Contains("StrictHostKeyChecking"));
        Assert.Empty(report.Probes);
    }

    [Fact]
    public async Task Findings_sorted_most_severe_first()
    {
        File.WriteAllText(_path, "Host target\n    ProxyJump missing\n    Compression yes\n");
        var report = await SshDoctor.CreateDefault().RunAsync(_path, "target", runProbes: false);

        Assert.NotEmpty(report.Findings);
        Assert.Equal(Severity.Error, report.Findings[0].Severity); // jump-host error ranks first
    }
}
