using MountTool.Diagnostics;

namespace MountTool.Core.Tests;

public class DiagnosticsLogTests
{
    [Fact]
    public void Recorded_lines_appear_in_bundle()
    {
        var log = new DiagnosticsLog(directory: null);
        log.Record("mount", "attempting connection");
        Assert.Contains("attempting connection", log.BuildBundle());
    }

    [Fact]
    public void Password_values_are_redacted()
    {
        var log = new DiagnosticsLog(directory: null);
        log.Record("auth", "args: password=SECRET user=jbloggs");
        var bundle = log.BuildBundle();
        Assert.DoesNotContain("SECRET", bundle);
        Assert.Contains("[redacted]", bundle);
    }

    [Fact]
    public void Ring_buffer_trims_oldest_entries()
    {
        var log = new DiagnosticsLog(directory: null);
        for (var i = 0; i < DiagnosticsLog.RingCapacity + 50; i++)
            log.Record("n", $"line{i}");

        var bundle = log.BuildBundle();
        Assert.DoesNotContain("line0 ", bundle + " ");
        Assert.Contains($"line{DiagnosticsLog.RingCapacity + 49}", bundle);
    }
}
