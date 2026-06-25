using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class NfsClientTests
{
    private const string Export = "/export";

    [Fact]
    public async Task Connect_ThenList_Root()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1]);
        fileSystem.CreateDirectory(fileSystem.Root, "sub");
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        IReadOnlyList<string> names = await client.ListAsync("/", Token);

        Assert.Equal(["a.txt", "sub"], names);
    }

    [Fact]
    public async Task WriteAllBytes_ThenReadAllBytes_RoundTrips()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        byte[] payload = Encoding.UTF8.GetBytes("hello high-level client");
        await client.WriteAllBytesAsync("greeting.txt", payload, Token);

        byte[] read = await client.ReadAllBytesAsync("greeting.txt", Token);
        Assert.Equal(payload, read);
    }

    [Fact]
    public async Task WriteAllBytes_OverwritesAndTruncates()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        await client.WriteAllBytesAsync("f", Encoding.UTF8.GetBytes("a long initial value"), Token);
        await client.WriteAllBytesAsync("f", Encoding.UTF8.GetBytes("short"), Token);

        Assert.Equal("short", Encoding.UTF8.GetString(await client.ReadAllBytesAsync("f", Token)));
    }

    [Fact]
    public async Task CreateDirectory_ThenWriteNestedFile()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        await client.CreateDirectoryAsync("docs", Token);
        await client.WriteAllBytesAsync("docs/note.txt", Encoding.UTF8.GetBytes("nested"), Token);

        Assert.Equal(["note.txt"], await client.ListAsync("docs", Token));
        Assert.Equal("nested", Encoding.UTF8.GetString(await client.ReadAllBytesAsync("docs/note.txt", Token)));
    }

    [Fact]
    public async Task Stat_ReportsTypeAndSize()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "data", [1, 2, 3, 4, 5]);
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        NfsFileAttributes attributes = await client.StatAsync("data", Token);

        Assert.Equal(NfsFileType.Regular, attributes.Type);
        Assert.Equal(5ul, attributes.Size);
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "trash", [0]);
        await using var server = StartServer(fileSystem);
        await using NfsClient client = await NfsClient.ConnectAsync(server.LocalEndPoint, Export, Token);

        await client.DeleteAsync("trash", Token);

        NfsException error = await Assert.ThrowsAsync<NfsException>(() => client.StatAsync("trash", Token).AsTask());
        Assert.Equal(NfsStatus.NoEntry, error.Status);
    }

    [Fact]
    public async Task Connect_UnknownExport_Throws()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);

        await Assert.ThrowsAsync<NfsException>(
            () => NfsClient.ConnectAsync(server.LocalEndPoint, "/does-not-exist", Token).AsTask());
    }

    [Fact]
    public async Task ProbeVersions_MultiVersionServer_ReportsAllThree()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);

        IReadOnlySet<uint> versions = await NfsClient.ProbeVersionsAsync(server.LocalEndPoint, Token);

        Assert.Equal([2u, 3u, 4u], versions.OrderBy(v => v).ToArray());
    }

    [Fact]
    public async Task ProbeVersions_V3OnlyServer_ReportsOnlyThree()
    {
        var fileSystem = new InMemoryFileSystem();
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs3Program(fileSystem));
        server.Start();
        await using var _ = server;

        IReadOnlySet<uint> versions = await NfsClient.ProbeVersionsAsync(server.LocalEndPoint, Token);

        Assert.Equal([3u], versions.ToArray());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(INfsFileSystem fileSystem)
    {
        // Host both the NFS program (100003) and the MOUNT program (100005) on one endpoint.
        var programs = new IRpcProgram[]
        {
            new NfsProgram(fileSystem),
            new Nfs3MountProgram(Export, fileSystem),
        };
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), programs);
        server.Start();
        return server;
    }
}
