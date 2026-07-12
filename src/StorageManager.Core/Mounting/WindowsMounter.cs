using System.Diagnostics;
using StorageManager.Errors;

namespace StorageManager.Mounting;

public sealed class WindowsMounter(Config config) : MounterBase(config)
{
    private const string DefaultSshfsPath = @"C:\Program Files\SSHFS-Win\bin\sshfs.exe";

    private string Drive => Config.MountTarget ?? "S:";

    public override string TargetDescription => Drive;

    protected override string MountTarget => Drive;

    protected override IEnumerable<string> ExtraSshfsArguments =>
        ["-o", "uid=-1,gid=-1", "-o", "ssh_command=/usr/bin/ssh.exe"];

    // ProxyJump makes OpenSSH generate a hardcoded "/usr/bin/ssh" proxy command,
    // which the bundled busybox shell (native Win32, not cygwin) can't resolve —
    // it doesn't map /usr/bin or append .exe. So drive the proxy ourselves with an
    // explicit, absolute ssh.exe path (forward-slashed, single-quoted for the space),
    // authenticating the jump hop via the same inherited SSH_ASKPASS as the target.
    protected override IEnumerable<string> ProxyArguments(string jumpHost, string username)
    {
        var sshExe = (FindSshfs() ?? DefaultSshfsPath)
            .Replace("sshfs.exe", "ssh.exe", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/');
        var proxy = $"'{sshExe}' -o StrictHostKeyChecking=accept-new -l {username} -W [%h]:%p {jumpHost}";
        return ["-o", $"ProxyCommand={proxy}"];
    }

    protected override void ConfigureEnvironment(ProcessStartInfo startInfo) =>
        startInfo.EnvironmentVariables["CYGFUSE"] = "WinFsp";

    protected override string? FindSshfs() =>
        File.Exists(DefaultSshfsPath) ? DefaultSshfsPath : FindOnPath("sshfs.exe");

    protected override bool TargetPresent()
    {
        try
        {
            return Directory.Exists(Drive + @"\");
        }
        catch
        {
            return false;
        }
    }

    public override PreflightResult? Preflight()
    {
        var sshfs = FindSshfs();
        if (sshfs is null)
            return new PreflightResult(
                "SSHFS-Win was not found.\n\n" +
                "It needs WinFsp and SSHFS-Win installed. Click \"Install for me\" to do this " +
                "automatically, or install manually:\n" +
                "1. WinFsp — https://winfsp.dev/rel/\n" +
                "2. SSHFS-Win — https://github.com/winfsp/sshfs-win/releases",
                new FixAction("Install for me", FixKindUi.WingetInstall,
                    "WinFsp.WinFsp;SSHFS-Win.SSHFS-Win"));

        // A jump host needs OpenSSH's ProxyJump, which runs its proxy via /bin/sh.
        // SSHFS-Win's cygwin bundle ships ssh.exe but no sh.exe, so we require a
        // dependency-free POSIX shell (busybox) beside it before a jump mount works.
        if (Config.JumpHost is not null)
        {
            var binDir = Path.GetDirectoryName(sshfs)!;
            var shell = Path.Combine(binDir, "sh.exe");
            if (!File.Exists(shell))
            {
                var command =
                    "Invoke-WebRequest -Uri https://frippery.org/files/busybox/busybox.exe " +
                    $"-OutFile \"{shell}\"";
                return new PreflightResult(
                    "Jump-host mounts on Windows need a small POSIX shell (sh.exe) next to SSHFS-Win, " +
                    "which its installer doesn't include (OpenSSH runs the jump connection through " +
                    "/bin/sh).\n\n" +
                    "To add it once: open Windows PowerShell as Administrator, paste the copied " +
                    "command, run it, then try connecting again.",
                    new FixAction("Copy install command", FixKindUi.CopyCommand, command));
            }
        }

        if (Environment.GetLogicalDrives().Contains(Drive + @"\"))
            return new PreflightResult($"{Drive} is already in use.");

        return null;
    }

    protected override async Task PlatformUnmountAsync()
    {
        // Kill sshfs.exe and its child ssh.exe; WinFsp then removes the drive letter.
        if (SshfsProcess is { } process)
            await Task.Run(() => RunQuiet(
                Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                "/PID", process.Id.ToString(), "/T", "/F"));
    }

    public override void OpenInFileManager() => OpenPath("explorer.exe", Drive + @"\");
}
