using System.Diagnostics;

namespace MountTool.Mounting;

public sealed class WindowsMounter(Config config) : MounterBase(config)
{
    private const string DefaultSshfsPath = @"C:\Program Files\SSHFS-Win\bin\sshfs.exe";

    private string Drive => Config.MountTarget ?? "S:";

    public override string TargetDescription => Drive;

    protected override string MountTarget => Drive;

    protected override IEnumerable<string> ExtraSshfsArguments =>
        ["-o", "uid=-1,gid=-1", "-o", "ssh_command=/usr/bin/ssh.exe"];

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

    public override string? Preflight()
    {
        if (FindSshfs() is null)
            return "SSHFS-Win was not found.\n\n" +
                   "Install both (in this order):\n" +
                   "1. WinFsp — https://winfsp.dev/rel/\n" +
                   "2. SSHFS-Win — https://github.com/winfsp/sshfs-win/releases";

        if (Environment.GetLogicalDrives().Contains(Drive + @"\"))
            return $"{Drive} is already in use.";

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
