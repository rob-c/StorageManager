using System.Net.Sockets;

namespace MountTool.Connectivity;

/// <summary>Outcome of a TCP reachability check against a gateway.</summary>
public sealed record ProbeResult(bool Reachable, string? Message);

/// <summary>
/// Cheap pre-mount reachability check. A plain TCP connect to the SSH port
/// tells us, before spawning sshfs, whether the host is reachable at all — the
/// usual cause of failure being an off-campus user who isn't on the VPN.
/// </summary>
public static class GatewayProbe
{
    public static async Task<ProbeResult> CheckAsync(
        string host, int port, TimeSpan timeout, CancellationToken ct = default)
    {
        using var client = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await client.ConnectAsync(host, port, timeoutCts.Token);
            return new ProbeResult(true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Unreachable(host);
        }
        catch (Exception)
        {
            return Unreachable(host);
        }
    }

    private static ProbeResult Unreachable(string host) =>
        new(false, $"Can't reach {host}. If you're off campus, connect to the " +
                   "University VPN first, then try again.");
}
