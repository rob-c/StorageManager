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
    private bool _createdDir;

    public bool IsMounted => MountPointLive(mountPoint);

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
        if (result.Ok && IsMounted)
            return null;

        CleanupDir();
        var detail = result.Stderr.Trim();
        return detail.Length > 0 ? detail : "sshfs could not mount the target.";
    }

    public async Task UnmountAsync(CancellationToken ct = default)
    {
        // fusermount on Linux, umount on macOS; both best-effort.
        if (OperatingSystem.IsLinux())
        {
            if (!(await runner.RunAsync("fusermount", ["-u", mountPoint], ct: ct)).Ok)
                await runner.RunAsync("fusermount3", ["-u", mountPoint], ct: ct);
        }
        else
        {
            await runner.RunAsync("umount", [mountPoint], ct: ct);
        }
        CleanupDir();
    }

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
