using System.Net;
using System.Net.Sockets;
using MountTool.Connectivity;

namespace MountTool.Core.Tests;

public class GatewayProbeTests
{
    [Fact]
    public async Task Reachable_host_returns_true()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var result = await GatewayProbe.CheckAsync("127.0.0.1", port, TimeSpan.FromSeconds(2));
            Assert.True(result.Reachable);
            Assert.Null(result.Message);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Unreachable_host_returns_vpn_message()
    {
        // Port 1 on loopback is closed; connect fails fast.
        var result = await GatewayProbe.CheckAsync("127.0.0.1", 1, TimeSpan.FromMilliseconds(500));
        Assert.False(result.Reachable);
        Assert.NotNull(result.Message);
        Assert.Contains("VPN", result.Message);
    }
}
