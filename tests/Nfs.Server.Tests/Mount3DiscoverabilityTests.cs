using System.Net;

using Nfs.Client;
using Nfs.Mount;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Mount3DiscoverabilityTests
{
    [Fact]
    public async Task ExportDumpAndUnmount_RoundTripOverRpc()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new Nfs3MountProgram("/export", fileSystem));
        server.Start();

        await using RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var mount = new Mount3Client(rpc, AuthSys.Create(uid: 0, gid: 0, machineName: "client1"));

        Mount3ExportList exports = await mount.ExportAsync(Token);
        Mount3ExportEntry export = Assert.Single(exports.Exports);
        Assert.Equal("/export", export.Directory);
        Assert.Empty(export.Groups);

        Mount3MountResult mounted = await mount.MountAsync("/export", Token);
        Assert.True(mounted.IsSuccess);

        Mount3MountList dumpAfterMount = await mount.DumpAsync(Token);
        Mount3MountEntry activeMount = Assert.Single(dumpAfterMount.Mounts);
        Assert.Equal("client1", activeMount.Hostname);
        Assert.Equal("/export", activeMount.Directory);

        await mount.UnmountAsync("/export", Token);

        Mount3MountList dumpAfterUnmount = await mount.DumpAsync(Token);
        Assert.Empty(dumpAfterUnmount.Mounts);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
