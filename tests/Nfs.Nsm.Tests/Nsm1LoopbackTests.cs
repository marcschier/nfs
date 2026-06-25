using System.Net;

using Nfs.Client;
using Nfs.Nsm;
using Nfs.Rpc;
using Nfs.Server;

using Xunit;

namespace Nfs.Nsm.Tests;

public sealed class Nsm1LoopbackTests
{
    [Fact]
    public async Task Monitor_ThenStat_Succeeds()
    {
        await using var server = StartServer();
        Nsm1Client nsm = await ConnectAsync(server);

        Nsm1StatusResult monitor = await nsm.MonitorAsync(new Nsm1Monitor
        {
            MonitorId = new Nsm1MonitorId
            {
                MonitorName = "remote-host",
                MyId = new Nsm1MyId
                {
                    MyName = "local-host",
                    Program = 100024,
                    Version = 1,
                    Procedure = 6,
                },
            },
            Private = new byte[Nsm1.PrivateLength],
        }, Token);
        Nsm1StatusResult stat = await nsm.StatAsync("remote-host", Token);

        Assert.Equal(Nsm1Result.Success, monitor.Result);
        Assert.Equal(Nsm1Result.Success, stat.Result);
        Assert.Equal(monitor.State, stat.State);
    }

    [Fact]
    public async Task Notify_IsDeliveredToRecoveryCallback()
    {
        var notified = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = StartServer(host => notified.SetResult(host));
        Nsm1Client nsm = await ConnectAsync(server);

        await nsm.NotifyAsync(new Nsm1StatusChange { MonitorName = "remote-host", State = 7 }, Token);

        Assert.Equal("remote-host", await notified.Task.WaitAsync(Token));
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(Action<string>? hostStateChanged = null)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nsm1Program(hostStateChanged));
        server.Start();
        return server;
    }

    private static async ValueTask<Nsm1Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nsm1Client(rpc);
    }
}
