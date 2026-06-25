using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Rpc;
using Nfs.Server;

using Xunit;

namespace Nfs.Client.Tests;

public sealed class NfsClientNegotiationTests
{
    [Fact]
    public async Task ConnectNegotiated_V3CapableServer_SelectsV3AndReads()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "hello.txt", "v3"u8.ToArray());
        await using var server = StartV3Server(fileSystem);

        await using NfsClient client = await NfsClient.ConnectNegotiatedAsync(server.LocalEndPoint, "/", cancellationToken: Token);

        Assert.Equal(NfsVersion.V3, client.NegotiatedVersion);
        Assert.Equal("v3", Encoding.UTF8.GetString(await client.ReadAllBytesAsync("hello.txt", Token)));
    }

    [Fact]
    public async Task ConnectNegotiated_V4CapableServer_SelectsV4AndReadsFromExport()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle export = fileSystem.CreateDirectory(fileSystem.Root, "export");
        fileSystem.CreateFile(export, "hello.txt", "v4"u8.ToArray());
        await using var server = StartV4Server(fileSystem);

        await using NfsClient client = await NfsClient.ConnectNegotiatedAsync(server.LocalEndPoint, "/export", cancellationToken: Token);

        Assert.Equal(NfsVersion.V4, client.NegotiatedVersion);
        NfsFileAttributes attributes = await client.StatAsync("hello.txt", Token);
        Assert.Equal(NfsFileType.Regular, attributes.Type);
        Assert.Equal("v4", Encoding.UTF8.GetString(await client.ReadAllBytesAsync("hello.txt", Token)));
    }

    [Fact]
    public async Task ConnectNegotiated_PreferredVersion_ForcesV3()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "hello.txt", "forced"u8.ToArray());
        await using var server = StartMultiVersionServer(fileSystem);

        await using NfsClient client = await NfsClient.ConnectNegotiatedAsync(
            server.LocalEndPoint,
            "/",
            NfsVersion.V3,
            Token);

        Assert.Equal(NfsVersion.V3, client.NegotiatedVersion);
        Assert.Equal("forced", Encoding.UTF8.GetString(await client.ReadAllBytesAsync("hello.txt", Token)));
    }

    [Fact]
    public async Task ConnectNegotiated_NoCommonVersion_ThrowsClearNfsException()
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new UnrelatedProgram());
        server.Start();
        await using var _ = server;

        NfsException exception = await Assert.ThrowsAsync<NfsException>(
            () => NfsClient.ConnectNegotiatedAsync(server.LocalEndPoint, "/", cancellationToken: Token).AsTask());
        Assert.Contains("mutually supported", exception.Message, StringComparison.Ordinal);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartV3Server(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            [new Nfs3Program(fileSystem), new Nfs3MountProgram("/", fileSystem)]);
        server.Start();
        return server;
    }

    private static RpcServer StartV4Server(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new NfsProgram(fileSystem));
        server.Start();
        return server;
    }

    private static RpcServer StartMultiVersionServer(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            [new NfsProgram(fileSystem), new Nfs3MountProgram("/", fileSystem)]);
        server.Start();
        return server;
    }

    private sealed class UnrelatedProgram : IRpcProgram
    {
        public uint Program => 999999;

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken) =>
            new(RpcReplyPayload.ProgramUnavailable());
    }
}
