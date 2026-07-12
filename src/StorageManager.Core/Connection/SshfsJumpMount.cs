using StorageManager.Ssh;

namespace StorageManager.Connection;

/// <summary>
/// SSHFS mount of a jump-host target on Unix. It relies on the ssh_config profile
/// (ProxyJump + ControlMaster + GSSAPI) written by <see cref="Ssh.SshProfileWriter"/>,
/// so sshfs's ssh reuses the shared master and authenticates via the delegated
/// Kerberos ticket. Best-effort on macOS; primary target is Linux.
/// </summary>
public sealed class SshfsJumpMount(
    IProcessRunner runner,
    string target,
    string user,
    string remotePath,
    string mountPoint,
    IReadOnlyDictionary<string, string>? environment = null) : IJumpMount
{
    private static readonly TimeSpan UnmountTimeout = TimeSpan.FromSeconds(10);
    private bool _createdDir;

    public async Task<bool> IsMountedAsync(CancellationToken ct = default)
    {
        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(mountPoint));
            if (File.Exists("/proc/mounts"))
                return File.ReadLines("/proc/mounts")
                    .Select(l => l.Split(' '))
                    .Any(f => f.Length > 1 && Unescape(f[1]) == target);
            // macOS: parse `mount` via the injected runner (no blocking subprocess on the UI thread).
            var r = await runner.RunAsync("/sbin/mount", [], stdin: null, null, TimeSpan.FromSeconds(5), ct);
            return r.Stdout.Contains($" on {target} (");
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> MountAsync(CancellationToken ct = default)
    {
        try
        {
            _createdDir = !Directory.Exists(mountPoint);
            Directory.CreateDirectory(mountPoint);
        }
        catch (Exception ex)
        {
            return $"Could not create the mount folder: {ex.Message}";
        }

        // sshfs daemonizes on success; ssh_config supplies ProxyJump/ControlMaster/GSSAPI.
        var args = new[]
        {
            $"{user}@{target}:{remotePath}",
            mountPoint,
            "-o", "reconnect=no",
            "-o", "ServerAliveInterval=5",
            "-o", "ServerAliveCountMax=3",
            "-o", "StrictHostKeyChecking=accept-new",
        };

        var result = await runner.RunAsync("sshfs", args, stdin: null, environment, TimeSpan.FromSeconds(60), ct);
        if (result.Ok && await IsMountedAsync(ct))
            return null;

        CleanupDir();
        var detail = result.Stderr.Trim();
        return detail.Length > 0 ? detail : "sshfs could not mount the target.";
    }

    public async Task UnmountAsync(CancellationToken ct = default)
    {
        // Bounded so a hung fusermount on a dead FUSE mount can't block teardown.
        if (OperatingSystem.IsLinux())
        {
            if (!(await Run("fusermount", ["-u", mountPoint], ct)).Ok
                && !(await Run("fusermount3", ["-u", mountPoint], ct)).Ok)
                await Run("fusermount", ["-uz", mountPoint], ct); // lazy detach as a last resort
        }
        else
        {
            if (!(await Run("umount", [mountPoint], ct)).Ok)
                await Run("umount", ["-f", mountPoint], ct);
        }
        CleanupDir();
    }

    private Task<ProcessResult> Run(string file, string[] args, CancellationToken ct) =>
        runner.RunAsync(file, args, stdin: null, null, UnmountTimeout, ct);

    private void CleanupDir()
    {
        try
        {
            if (_createdDir && Directory.Exists(mountPoint)
                && !MountPointLive(mountPoint)
                && !Directory.EnumerateFileSystemEntries(mountPoint).Any())
                Directory.Delete(mountPoint);
        }
        catch { /* leave the empty dir */ }
        _createdDir = false;
    }

    private static bool MountPointLive(string path)
    {
        try
        {
            var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (File.Exists("/proc/mounts"))
                return File.ReadLines("/proc/mounts")
                    .Select(l => l.Split(' '))
                    .Any(f => f.Length > 1 && Unescape(f[1]) == target);
            // Non-Linux (macOS): parse `mount` output for " on <target> (".
            return MountTableContains(target);
        }
        catch
        {
            return false;
        }
    }

    private static bool MountTableContains(string target)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo("/sbin/mount")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var p = System.Diagnostics.Process.Start(info);
            if (p is null)
                return false;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return output.Contains($" on {target} (");
        }
        catch
        {
            return false;
        }
    }

    private static string Unescape(string p) =>
        p.Replace(@"\040", " ").Replace(@"\011", "\t").Replace(@"\012", "\n").Replace(@"\134", @"\");
}
