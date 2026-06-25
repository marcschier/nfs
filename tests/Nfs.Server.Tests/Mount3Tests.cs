using System.Net;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Mount;
using Nfs.Protocol.V3;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Mount3Tests
{
    [Fact]
    public async Task Mount_ReturnsRootHandle_UsableWithNfs()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "hello.txt", [1, 2, 3, 4, 5]);

        await using var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new IRpcProgram[] { new Nfs3Program(fileSystem), new Nfs3MountProgram("/", fileSystem) });
        server.Start();

        await using RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var mount = new Mount3Client(rpc);
        var nfs = new Nfs3Client(rpc);

        Mount3MountResult mounted = await mount.MountAsync("/", Token);
        Assert.True(mounted.IsSuccess);
        Assert.Contains(1, mounted.Ok.AuthFlavors); // AUTH_SYS is offered
        Assert.Equal(fileSystem.Root.ToArray(), mounted.Ok.Handle);

        // The handle from MOUNT works against the NFS program on the same server.
        var root = new Nfs3Handle { Data = mounted.Ok.Handle };
        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(root, Token);
        Assert.True(attributes.IsSuccess);
        Assert.Equal(NfsFileType.Directory, attributes.Attributes.Type);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "hello.txt", Token);
        Assert.True(lookup.IsSuccess);
    }

    [Fact]
    public async Task Mount_UnknownExport_ReturnsNoEntry()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0), new Nfs3MountProgram("/", fileSystem));
        server.Start();

        await using RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var mount = new Mount3Client(rpc);

        Mount3MountResult mounted = await mount.MountAsync("/does-not-exist", Token);

        Assert.False(mounted.IsSuccess);
        Assert.Equal(Mount3Status.NoEntry, mounted.Status);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
}
