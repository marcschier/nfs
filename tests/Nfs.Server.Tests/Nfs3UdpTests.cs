using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V3;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Nfs3UdpTests
{
    [Fact]
    public async Task GetAttributes_OverUdp()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        await using RpcUdpClient rpc = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);
        var nfs = new Nfs3Client(rpc);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(NfsFileType.Directory, result.Attributes.Type);
    }

    [Fact]
    public async Task Create_Write_Read_OverUdp()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        await using RpcUdpClient rpc = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);
        var nfs = new Nfs3Client(rpc);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult create = await nfs.CreateAsync(root, "data.bin", Nfs3SetAttributes.None, Token);
        Assert.True(create.IsSuccess);
        Nfs3Handle file = create.Ok.Handle!.Value;

        byte[] payload = Encoding.UTF8.GetBytes("over udp");
        Nfs3WriteResult write = await nfs.WriteAsync(file, 0, payload, Nfs3StableHow.FileSync, Token);
        Assert.True(write.IsSuccess);

        Nfs3ReadResult read = await nfs.ReadAsync(file, 0, 1024, Token);
        Assert.Equal(payload, read.Ok.Data);
    }

    [Fact]
    public async Task ReadDirectory_OverUdp()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1]);
        fileSystem.CreateFile(fileSystem.Root, "b.txt", [2]);
        await using var server = StartServer(fileSystem);
        await using RpcUdpClient rpc = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);
        var nfs = new Nfs3Client(rpc);

        Nfs3ReadDirResult result = await nfs.ReadDirectoryAsync(ToWire(fileSystem.Root), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(["a.txt", "b.txt"], result.Ok.Entries.Select(e => e.Name).ToArray());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcUdpServer StartServer(INfsFileSystem fileSystem)
    {
        var server = new RpcUdpServer(new IPEndPoint(IPAddress.Loopback, 0), new NfsProgram(fileSystem));
        server.Start();
        return server;
    }

    private static Nfs3Handle ToWire(NfsFileHandle handle) => new() { Data = handle.ToArray() };
}
