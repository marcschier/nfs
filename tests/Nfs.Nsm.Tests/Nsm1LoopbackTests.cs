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

    [Fact]
    public async Task Null_Succeeds()
    {
        await using var server = StartServer();
        Nsm1Client nsm = await ConnectAsync(server);

        await nsm.NullAsync(Token); // Completes without throwing.
    }

    [Fact]
    public async Task Unmonitor_RemovesMonitorAndAdvancesState()
    {
        await using var server = StartServer();
        Nsm1Client nsm = await ConnectAsync(server);

        Nsm1StatusResult monitor = await nsm.MonitorAsync(BuildMonitor("remote-host"), Token);
        Nsm1Status unmonitor = await nsm.UnmonitorAsync("remote-host", Token);
        Nsm1StatusResult stat = await nsm.StatAsync("remote-host", Token);

        Assert.Equal(Nsm1Result.Success, monitor.Result);
        Assert.NotEqual(monitor.State, unmonitor.State); // Unmonitor bumps the NSM state counter.
        Assert.Equal(unmonitor.State, stat.State);        // A later Stat reflects the post-unmonitor state.
    }

    [Fact]
    public async Task UnmonitorAll_RemovesCallerMonitorsAndAdvancesState()
    {
        await using var server = StartServer();
        Nsm1Client nsm = await ConnectAsync(server);

        Nsm1StatusResult monitor = await nsm.MonitorAsync(BuildMonitor("remote-host"), Token);
        Nsm1Status unmonitor = await nsm.UnmonitorAllAsync(CallerId, Token);
        Nsm1StatusResult stat = await nsm.StatAsync("remote-host", Token);

        Assert.Equal(Nsm1Result.Success, monitor.Result);
        Assert.True(unmonitor.State > monitor.State); // UnmonitorAll bumps the NSM state counter.
        Assert.Equal(unmonitor.State, stat.State);
    }

    [Fact]
    public async Task SimulateCrash_AdvancesStateNumber()
    {
        await using var server = StartServer();
        Nsm1Client nsm = await ConnectAsync(server);

        Nsm1StatusResult monitor = await nsm.MonitorAsync(BuildMonitor("remote-host"), Token);
        await nsm.SimulateCrashAsync(Token); // Completes without throwing.
        Nsm1StatusResult stat = await nsm.StatAsync("remote-host", Token);

        Assert.Equal(Nsm1Result.Success, monitor.Result);
        Assert.True(stat.State > monitor.State); // A simulated crash bumps the NSM state counter.
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

    private static Nsm1MyId CallerId => new()
    {
        MyName = "local-host",
        Program = 100024,
        Version = 1,
        Procedure = 6,
    };

    private static Nsm1Monitor BuildMonitor(string host) => new()
    {
        MonitorId = new Nsm1MonitorId
        {
            MonitorName = host,
            MyId = CallerId,
        },
        Private = new byte[Nsm1.PrivateLength],
    };
}
