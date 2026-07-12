using MountTool;
using MountTool.Mounting;

namespace MountTool.Core.Tests;

public class PreflightTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "preflight-" + Guid.NewGuid().ToString("N"));

    public PreflightTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static IMounter UnixMounterFor(string target) =>
        new LinuxMounter(Config.Default with { RemotePath = "/home/x", MountTarget = target });

    [Fact]
    public void Empty_target_directory_passes_preflight()
    {
        // Requires sshfs present (true on the Linux CI/dev box).
        if (new LinuxMounter(Config.Default with { MountTarget = _dir }).Preflight()?.Message
                is { } m && m.Contains("sshfs was not found"))
            return; // environment without sshfs — skip

        var target = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(target);
        Assert.Null(UnixMounterFor(target).Preflight());
    }

    [Fact]
    public void Non_empty_target_is_a_non_blocking_warning()
    {
        var target = Path.Combine(_dir, "full");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "data");

        var result = UnixMounterFor(target).Preflight();

        // Skip if sshfs isn't installed (that would block first).
        if (result?.Message.Contains("sshfs was not found") == true)
            return;

        Assert.NotNull(result);
        Assert.False(result!.Blocking);            // warn, don't block
        Assert.Contains("not empty", result.Message);
    }
}
