using System.Diagnostics;

namespace StorageManager.Mounting;

public sealed class MacMounter(Config config) : UnixMounterBase(config)
{
    protected override string InstallGuidance =>
        "Install macFUSE and sshfs:\n" +
        "  brew install macfuse\n" +
        "  brew install gromgit/fuse/sshfs-mac\n\n" +
        "macFUSE needs a one-time approval in System Settings → Privacy & Security\n" +
        "(administrator required), then a restart.";

    protected override string? InstallCommand => "brew install macfuse gromgit/fuse/sshfs-mac";

    // Prefer macFUSE's FSKit backend (user space, no kernel-extension approval).
    protected override IEnumerable<string> ExtraSshfsArguments =>
        Config.MacFskitBackend ? ["-o", "backend=fskit"] : [];

    protected override string? FindSshfs() =>
        FindOnPath("sshfs", "/usr/local/bin", "/opt/homebrew/bin");

    protected override bool IsMountPoint()
    {
        try
        {
            var info = new ProcessStartInfo("/sbin/mount")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var process = Process.Start(info);
            if (process is null)
                return false;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Contains($" on {Path.TrimEndingDirectorySeparator(Path.GetFullPath(Target))} (");
        }
        catch
        {
            return false;
        }
    }

    protected override Task PlatformUnmountAsync() => Task.Run(() =>
    {
        if (RunQuiet("/sbin/umount", Target) == 0)
            return;
        RunQuiet("/usr/sbin/diskutil", "unmount", "force", Target);
    });

    public override void OpenInFileManager() => OpenPath("/usr/bin/open", Target);
}
