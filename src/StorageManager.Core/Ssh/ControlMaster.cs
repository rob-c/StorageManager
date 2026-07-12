namespace StorageManager.Ssh;

/// <summary>
/// Manages the shared SSH master connection to a target. Establishing it opens a
/// backgrounded, multiplexed connection (through the jump host, per ssh_config);
/// the mount and the user's own <c>ssh</c> then ride the same socket. Liveness is
/// checked with <c>ssh -O check</c>, and it's torn down with <c>ssh -O exit</c>.
/// </summary>
public sealed class ControlMaster(IProcessRunner runner)
{
    private static readonly TimeSpan EstablishTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Starts the master: <c>ssh -M -N -f -o BatchMode=yes &lt;target&gt;</c>. With a valid
    /// Kerberos ticket this authenticates non-interactively via GSSAPI through the jump.
    /// </summary>
    public Task<ProcessResult> EstablishAsync(
        string target,
        string? configPath = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken ct = default)
    {
        var args = new List<string> { "-M", "-N", "-f", "-o", "BatchMode=yes" };
        AddConfig(args, configPath);
        args.Add(target);
        return runner.RunAsync("ssh", args, stdin: null, environment, EstablishTimeout, ct);
    }

    /// <summary>True when a live master exists for the target (<c>ssh -O check</c> exit 0).</summary>
    public async Task<bool> IsAliveAsync(string target, string? configPath = null, CancellationToken ct = default)
    {
        var args = new List<string> { "-O", "check" };
        AddConfig(args, configPath);
        args.Add(target);
        var result = await runner.RunAsync("ssh", args, stdin: null, null, ControlTimeout, ct);
        return result.Ok;
    }

    /// <summary>Tears the master down (<c>ssh -O exit</c>); best-effort, never throws.</summary>
    public async Task ExitAsync(string target, string? configPath = null, CancellationToken ct = default)
    {
        try
        {
            var args = new List<string> { "-O", "exit" };
            AddConfig(args, configPath);
            args.Add(target);
            await runner.RunAsync("ssh", args, stdin: null, null, ControlTimeout, ct);
        }
        catch
        {
            // Tearing down a socket that's already gone is fine.
        }
    }

    private static void AddConfig(List<string> args, string? configPath)
    {
        if (!string.IsNullOrEmpty(configPath))
        {
            args.Add("-F");
            args.Add(configPath);
        }
    }
}
