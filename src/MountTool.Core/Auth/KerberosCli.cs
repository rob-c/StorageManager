using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MountTool.Auth;

/// <summary>
/// Real Kerberos CLI driver. Locates kinit/klist/aklog/kdestroy on PATH and in
/// the standard per-OS Kerberos install directories. Feeding kinit a password
/// programmatically is best-effort (portability varies); the helper verifies
/// success by re-reading the ticket cache afterwards.
/// </summary>
public sealed class KerberosCli : IKerberosCli
{
    private readonly string? _kinit;
    private readonly string? _klist;
    private readonly string? _aklog;
    private readonly string? _kdestroy;

    public KerberosCli()
    {
        _kinit = Locate("kinit");
        _klist = Locate("klist");
        _aklog = Locate("aklog");
        _kdestroy = Locate("kdestroy");
    }

    public bool ToolsAvailable => _kinit is not null && _klist is not null;
    public bool HasAklog => _aklog is not null;

    public bool HasValidTicket() => _klist is not null && RunExit(_klist, "-s") == 0;

    public string? GetKlistOutput() => _klist is null ? null : RunOutput(_klist);

    public bool Kinit(string principal, string password)
    {
        if (_kinit is null)
            return false;
        try
        {
            var info = new ProcessStartInfo(_kinit)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add(principal);
            using var p = Process.Start(info);
            if (p is null)
                return false;
            p.StandardInput.WriteLine(password);
            p.StandardInput.Close();
            p.WaitForExit(30_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Aklog() => _aklog is not null && RunExit(_aklog) == 0;
    public bool Kdestroy() => _kdestroy is not null && RunExit(_kdestroy) == 0;

    private static int RunExit(string exe, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return -1;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            return p.ExitCode;
        }
        catch { return -1; }
    }

    private static string? RunOutput(string exe, params string[] args)
    {
        try
        {
            var info = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using var p = Process.Start(info);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit(15_000);
            return output;
        }
        catch { return null; }
    }

    private static string? Locate(string tool)
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? tool + ".exe" : tool;

        var dirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            dirs.Add(Path.Combine(pf, @"MIT\Kerberos\bin"));
        }
        else
        {
            dirs.AddRange(["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin", "/usr/kerberos/bin"]);
        }

        return dirs.Select(d => Path.Combine(d, exe)).FirstOrDefault(File.Exists);
    }
}
