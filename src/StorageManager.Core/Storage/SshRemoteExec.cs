using System.Diagnostics;

namespace StorageManager.Storage;

/// <summary>
/// Runs a remote command via the system `ssh`, non-interactively. With
/// <c>useGssapi</c> (the app-wide Kerberos switch) it prefers GSSAPI so a valid
/// ticket needs no password; otherwise it sticks to public-key auth only.
/// BatchMode ensures it fails fast rather than hanging on a prompt when no
/// non-interactive credential is available.
/// </summary>
public sealed class SshRemoteExec(bool useGssapi = false) : IRemoteExec
{
    public async Task<RemoteResult> RunAsync(string host, string user, string command, CancellationToken ct = default)
    {
        try
        {
            var info = new ProcessStartInfo("ssh")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var args = new List<string>
            {
                "-o", "BatchMode=yes",
                "-o", "ConnectTimeout=10",
                "-o", "StrictHostKeyChecking=accept-new",
            };
            if (useGssapi)
            {
                args.AddRange(["-o", "GSSAPIAuthentication=yes",
                               "-o", "PreferredAuthentications=gssapi-with-mic,publickey"]);
            }
            else
            {
                args.AddRange(["-o", "GSSAPIAuthentication=no",
                               "-o", "PreferredAuthentications=publickey"]);
            }
            args.AddRange([$"{user}@{host}", "--", command]);
            foreach (var a in args)
                info.ArgumentList.Add(a);

            using var p = Process.Start(info);
            if (p is null)
                return new RemoteResult(-1, "", "ssh did not start");

            var stdout = p.StandardOutput.ReadToEndAsync(ct);
            var stderr = p.StandardError.ReadToEndAsync(ct);
            await p.WaitForExitAsync(ct);
            return new RemoteResult(p.ExitCode, await stdout, await stderr);
        }
        catch (Exception ex)
        {
            return new RemoteResult(-1, "", ex.Message);
        }
    }
}
