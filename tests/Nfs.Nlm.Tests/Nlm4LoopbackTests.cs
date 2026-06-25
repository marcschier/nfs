using System.Net;

using Nfs.Client;
using Nfs.Nlm;
using Nfs.Nsm;
using Nfs.Rpc;
using Nfs.Server;

using Xunit;

namespace Nfs.Nlm.Tests;

public sealed class Nlm4LoopbackTests
{
    private static readonly byte[] FileA = [1, 2, 3, 4];
    private static readonly byte[] FileB = [9, 9, 9, 9];

    [Fact]
    public async Task Null_Succeeds()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        await nlm.NullAsync(Token);
    }

    [Fact]
    public async Task Lock_ThenConflictingLock_FromAnotherOwner_Denied()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        Nlm4Res first = await nlm.LockAsync(MakeLock(FileA, owner: "owner-1", svid: 1, 0, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Granted, first.Status);

        Nlm4Res second = await nlm.LockAsync(MakeLock(FileA, owner: "owner-2", svid: 2, 5, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Denied, second.Status);
    }

    [Fact]
    public async Task Test_ReportsHolderWhenDenied()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        await nlm.LockAsync(MakeLock(FileA, "owner-1", 1, 0, 10), exclusive: true, Token);

        Nlm4TestRes test = await nlm.TestAsync(MakeLock(FileA, "owner-2", 2, 5, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Denied, test.Status);
        Assert.NotNull(test.Holder);
        Assert.Equal(1, test.Holder!.Value.ServerId);
        Assert.True(test.Holder.Value.Exclusive);
    }

    [Fact]
    public async Task Unlock_ReleasesLock_AllowingAnotherOwner()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        await nlm.LockAsync(MakeLock(FileA, "owner-1", 1, 0, 10), exclusive: true, Token);
        Nlm4Res unlock = await nlm.UnlockAsync(MakeLock(FileA, "owner-1", 1, 0, 10), Token);
        Assert.Equal(Nlm4Status.Granted, unlock.Status);

        Nlm4Res retry = await nlm.LockAsync(MakeLock(FileA, "owner-2", 2, 0, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Granted, retry.Status);
    }

    [Fact]
    public async Task SharedLocks_DoNotConflict()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        Nlm4Res first = await nlm.LockAsync(MakeLock(FileA, "owner-1", 1, 0, 10), exclusive: false, Token);
        Nlm4Res second = await nlm.LockAsync(MakeLock(FileA, "owner-2", 2, 0, 10), exclusive: false, Token);

        Assert.Equal(Nlm4Status.Granted, first.Status);
        Assert.Equal(Nlm4Status.Granted, second.Status);
    }

    [Fact]
    public async Task LocksOnDifferentFiles_DoNotConflict()
    {
        await using var server = StartServer();
        Nlm4Client nlm = await ConnectAsync(server);

        Nlm4Res a = await nlm.LockAsync(MakeLock(FileA, "owner-1", 1, 0, 10), exclusive: true, Token);
        Nlm4Res b = await nlm.LockAsync(MakeLock(FileB, "owner-2", 2, 0, 10), exclusive: true, Token);

        Assert.Equal(Nlm4Status.Granted, a.Status);
        Assert.Equal(Nlm4Status.Granted, b.Status);
    }

    [Fact]
    public async Task BlockingLock_IsGrantedByCallback_AfterUnlock()
    {
        await using var server = StartServer();
        await using Nlm4CallbackHost callback = Nlm4CallbackHost.Start();
        Nlm4Client nlm = await ConnectAsync(server);

        Nlm4Lock held = MakeLock(FileA, "owner-1", 1, 0, 10, callerName: "holder");
        Nlm4Lock blocked = MakeLock(FileA, "owner-2", 2, 0, 10, CallbackName(callback));
        await nlm.LockAsync(held, exclusive: true, Token);

        Nlm4Res blockedResult = await nlm.LockAsync(blocked, exclusive: true, block: true, Token);
        Assert.Equal(Nlm4Status.Blocked, blockedResult.Status);

        ValueTask<Nlm4Lock> grant = callback.WaitForGrantedAsync(Token);
        await nlm.UnlockAsync(held, Token);

        Nlm4Lock granted = await grant;
        Assert.Equal(blocked.Owner, granted.Owner);
        Assert.Equal(blocked.ServerId, granted.ServerId);

        Nlm4Res conflict = await nlm.LockAsync(MakeLock(FileA, "owner-3", 3, 0, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Denied, conflict.Status);
    }

    [Fact]
    public async Task Cancel_RemovesPendingBlockingLock()
    {
        await using var server = StartServer();
        await using Nlm4CallbackHost callback = Nlm4CallbackHost.Start();
        Nlm4Client nlm = await ConnectAsync(server);

        Nlm4Lock held = MakeLock(FileA, "owner-1", 1, 0, 10, callerName: "holder");
        Nlm4Lock blocked = MakeLock(FileA, "owner-2", 2, 0, 10, CallbackName(callback));
        await nlm.LockAsync(held, exclusive: true, Token);
        Nlm4Res blockedResult = await nlm.LockAsync(blocked, exclusive: true, block: true, Token);
        Assert.Equal(Nlm4Status.Blocked, blockedResult.Status);

        Nlm4Res cancel = await nlm.CancelAsync(blocked, exclusive: true, Token);
        Assert.Equal(Nlm4Status.Granted, cancel.Status);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        ValueTask<Nlm4Lock> grant = callback.WaitForGrantedAsync(timeout.Token);
        await nlm.UnlockAsync(held, Token);

        await Assert.ThrowsAsync<TaskCanceledException>(async () => await grant);
        Nlm4Res retry = await nlm.LockAsync(blocked, exclusive: true, Token);
        Assert.Equal(Nlm4Status.Granted, retry.Status);
    }

    [Fact]
    public async Task NotifyRecovery_DropsHostLocks()
    {
        var nlmProgram = new Nlm4Program();
        await using var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new IRpcProgram[] { nlmProgram, new Nsm1Program(nlmProgram.DropLocksForHost) });
        server.Start();
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var nlm = new Nlm4Client(rpc);
        var nsm = new Nsm1Client(rpc);

        Nlm4Lock held = MakeLock(FileA, "owner-1", 1, 0, 10, callerName: "rebooted-host");
        await nlm.LockAsync(held, exclusive: true, Token);
        Nlm4Res denied = await nlm.LockAsync(MakeLock(FileA, "owner-2", 2, 0, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Denied, denied.Status);

        await nsm.NotifyAsync(new Nsm1StatusChange { MonitorName = "rebooted-host", State = 3 }, Token);

        Nlm4Res granted = await nlm.LockAsync(MakeLock(FileA, "owner-2", 2, 0, 10), exclusive: true, Token);
        Assert.Equal(Nlm4Status.Granted, granted.Status);
    }

    private static Nlm4Lock MakeLock(byte[] file, string owner, int svid, ulong offset, ulong length) => new()
    {
        CallerName = "test-host",
        FileHandle = file,
        Owner = System.Text.Encoding.UTF8.GetBytes(owner),
        ServerId = svid,
        Offset = offset,
        Length = length,
    };

    private static Nlm4Lock MakeLock(
        byte[] file,
        string owner,
        int svid,
        ulong offset,
        ulong length,
        string callerName) => new()
        {
            CallerName = callerName,
            FileHandle = file,
            Owner = System.Text.Encoding.UTF8.GetBytes(owner),
            ServerId = svid,
            Offset = offset,
            Length = length,
        };

    private static string CallbackName(Nlm4CallbackHost callback) =>
        $"{callback.EndPoint.Address}:{callback.EndPoint.Port}";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer()
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nlm4Program());
        server.Start();
        return server;
    }

    private static async ValueTask<Nlm4Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nlm4Client(rpc);
    }
}
