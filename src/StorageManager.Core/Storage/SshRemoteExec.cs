using System.Diagnostics;

namespace StorageManager.Storage;

/// <summary>
/// Runs a remote command via the system `ssh`, non-interactively. Prefers
/// GSSAPI (Kerberos) and public-key auth so that, given a valid ticket, no
/// password is needed. BatchMode ensures it fails fast rather than hanging on a
/// prompt when no non-interactive credential is available.
/// </summary>
public sealed class SshRemoteExec : IRemoteExec
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
            foreach (var a in new[]
            {
                "-o", "BatchMode=yes",
                "-o", "ConnectTimeout=10",
                "-o", "StrictHostKeyChecking=accept-new",
                "-o", "GSSAPIAuthentication=yes",
                "-o", "PreferredAuthentications=gssapi-with-mic,publickey",
                $"{user}@{host}", "--", command,
            })
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
