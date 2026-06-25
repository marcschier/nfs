using System.Net;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class PortmapServerTests
{
    [Fact]
    public async Task GetPort_ReturnsRegisteredPort()
    {
        var portmap = new PortmapServer();
        portmap.Register(program: 100003, version: 3, PortmapProtocol.Tcp, port: 2049);
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        int port = await PortmapClient.GetPortAsync(client, 100003, 3, PortmapProtocol.Tcp, Token);

        Assert.Equal(2049, port);
    }

    [Fact]
    public async Task GetPort_ForUnknownProgram_ReturnsZero()
    {
        var portmap = new PortmapServer();
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        int port = await PortmapClient.GetPortAsync(client, 999999, 1, PortmapProtocol.Tcp, Token);

        Assert.Equal(0, port);
    }

    [Fact]
    public async Task Unregister_RemovesTheMapping()
    {
        var portmap = new PortmapServer();
        portmap.Register(100005, 3, PortmapProtocol.Tcp, 635);
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        Assert.Equal(635, await PortmapClient.GetPortAsync(client, 100005, 3, PortmapProtocol.Tcp, Token));

        portmap.Unregister(100005, 3, PortmapProtocol.Tcp);
        Assert.Equal(0, await PortmapClient.GetPortAsync(client, 100005, 3, PortmapProtocol.Tcp, Token));
    }

    [Fact]
    public async Task DistinctProtocols_AreSeparateMappings()
    {
        var portmap = new PortmapServer();
        portmap.Register(100003, 3, PortmapProtocol.Tcp, 2049);
        portmap.Register(100003, 3, PortmapProtocol.Udp, 2050);
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        Assert.Equal(2049, await PortmapClient.GetPortAsync(client, 100003, 3, PortmapProtocol.Tcp, Token));
        Assert.Equal(2050, await PortmapClient.GetPortAsync(client, 100003, 3, PortmapProtocol.Udp, Token));
    }

    [Fact]
    public async Task SetDumpAndUnset_RoundTripOverRpc()
    {
        var portmap = new PortmapServer();
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        Assert.True(await PortmapClient.SetAsync(client, 100003, 3, PortmapProtocol.Tcp, 2049, Token));
        Assert.Equal(2049, await PortmapClient.GetPortAsync(client, 100003, 3, PortmapProtocol.Tcp, Token));

        PortmapMapping mapping = Assert.Single(await PortmapClient.DumpAsync(client, Token));
        Assert.Equal(100003u, mapping.Program);
        Assert.Equal(3u, mapping.Version);
        Assert.Equal((uint)PortmapProtocol.Tcp, mapping.Protocol);
        Assert.Equal(2049u, mapping.Port);

        Assert.True(await PortmapClient.UnsetAsync(client, 100003, 3, PortmapProtocol.Tcp, cancellationToken: Token));
        Assert.Empty(await PortmapClient.DumpAsync(client, Token));
        Assert.Equal(0, await PortmapClient.GetPortAsync(client, 100003, 3, PortmapProtocol.Tcp, Token));
    }

    [Fact]
    public async Task PortmapRegistration_RegistersAndUnregistersMapping()
    {
        var portmap = new PortmapServer();
        await using var server = StartServer(portmap);
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);

        await using (await PortmapRegistration.RegisterAsync(
            server.LocalEndPoint,
            100005,
            3,
            PortmapProtocol.Tcp,
            635,
            Token))
        {
            Assert.Equal(635, await PortmapClient.GetPortAsync(client, 100005, 3, PortmapProtocol.Tcp, Token));
        }

        Assert.Equal(0, await PortmapClient.GetPortAsync(client, 100005, 3, PortmapProtocol.Tcp, Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(PortmapServer portmap)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), portmap);
        server.Start();
        return server;
    }
}
