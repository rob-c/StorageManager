using System.Net.Sockets;

namespace StorageManager.Doctor;

/// <summary>
/// Active network probe. Confirms TCP reach, watches for the SSH banner (a
/// silent stall mid-handshake is a classic DPI signature), and optionally holds
/// the socket idle to measure when a middlebox resets it.
/// </summary>
public sealed class DoctorProbe : IDoctorProbe
{
    private readonly TimeSpan _bannerTimeout;
    private readonly TimeSpan _idleHold;

    public DoctorProbe(TimeSpan? bannerTimeout = null, TimeSpan? idleHold = null)
    {
        _bannerTimeout = bannerTimeout ?? TimeSpan.FromSeconds(5);
        _idleHold = idleHold ?? TimeSpan.FromMinutes(3);
    }

    public async Task<ProbeOutcome> ProbeAsync(string host, int port, bool idleTest, CancellationToken ct)
    {
        using var client = new TcpClient();
        try
        {
            using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                connectCts.CancelAfter(_bannerTimeout);
                await client.ConnectAsync(host, port, connectCts.Token);
            }
        }
        catch
        {
            return new ProbeOutcome(host, port, Reachable: false, BannerSeen: false, IdleResetAfter: null);
        }

        var bannerSeen = await ReadBannerAsync(client, ct);

        TimeSpan? idleReset = null;
        if (idleTest)
            idleReset = await MeasureIdleResetAsync(client, ct);

        return new ProbeOutcome(host, port, Reachable: true, bannerSeen, idleReset);
    }

    private async Task<bool> ReadBannerAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            var stream = client.GetStream();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_bannerTimeout);
            var buffer = new byte[64];
            var read = await stream.ReadAsync(buffer, cts.Token);
            var text = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Max(0, read));
            return text.StartsWith("SSH-", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task<TimeSpan?> MeasureIdleResetAsync(TcpClient client, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        try
        {
            var stream = client.GetStream();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_idleHold);
            var buffer = new byte[1];
            var read = await stream.ReadAsync(buffer, cts.Token);
            // A 0-byte read means the peer closed the connection while idle.
            return read == 0 ? DateTime.UtcNow - start : null;
        }
        catch (OperationCanceledException)
        {
            return null; // survived the full hold
        }
        catch
        {
            return DateTime.UtcNow - start; // reset by peer/middlebox
        }
    }
}
