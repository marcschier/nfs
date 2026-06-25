using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V2;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Nfs2ServerTests
{
    [Fact]
    public async Task Root_ReportsDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);

        Nfs2AttrStat result = await nfs.GetAttributesAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(NfsFileType.Directory, result.Attributes.Type);
    }

    [Fact]
    public async Task Lookup_ThenGetAttributes_OnAFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "readme.txt", Encoding.UTF8.GetBytes("hello world"));
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);

        Nfs2DirOpResult lookup = await nfs.LookupAsync(ToWire(fileSystem.Root), "readme.txt", Token);
        Assert.True(lookup.IsSuccess);
        Assert.Equal(NfsFileType.Regular, lookup.Attributes.Type);
        Assert.Equal(11u, lookup.Attributes.Size);
    }

    [Fact]
    public async Task Create_Write_Read_RoundTrips()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle root = ToWire(fileSystem.Root);

        Nfs2DirOpResult create = await nfs.CreateAsync(root, "data.bin", cancellationToken: Token);
        Assert.True(create.IsSuccess);

        byte[] payload = Encoding.UTF8.GetBytes("the quick brown fox");
        Nfs2AttrStat write = await nfs.WriteAsync(create.Handle, 0, payload, Token);
        Assert.True(write.IsSuccess);
        Assert.Equal((uint)payload.Length, write.Attributes.Size);

        Nfs2ReadResult read = await nfs.ReadAsync(create.Handle, 0, 1024, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal(payload, read.Data);
    }

    [Fact]
    public async Task MakeDirectory_ThenLookup_Works()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle root = ToWire(fileSystem.Root);

        Nfs2DirOpResult mkdir = await nfs.MakeDirectoryAsync(root, "sub", cancellationToken: Token);
        Assert.True(mkdir.IsSuccess);
        Assert.Equal(NfsFileType.Directory, mkdir.Attributes.Type);

        Nfs2DirOpResult lookup = await nfs.LookupAsync(root, "sub", Token);
        Assert.True(lookup.IsSuccess);
    }

    [Fact]
    public async Task Remove_DeletesFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "f", [1]);
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle root = ToWire(fileSystem.Root);

        Nfs2StatResult remove = await nfs.RemoveAsync(root, "f", Token);
        Assert.True(remove.IsSuccess);
        Assert.Equal(NfsStatus.NoEntry, (await nfs.LookupAsync(root, "f", Token)).Status);
    }

    [Fact]
    public async Task Rename_MovesFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "from", [1, 2]);
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle root = ToWire(fileSystem.Root);

        Nfs2StatResult rename = await nfs.RenameAsync(root, "from", root, "to", Token);
        Assert.True(rename.IsSuccess);
        Assert.True((await nfs.LookupAsync(root, "to", Token)).IsSuccess);
    }

    [Fact]
    public async Task SetAttributes_ChangesMode()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle wire = ToWire(file);

        Nfs2SetAttributes changes = Nfs2SetAttributes.None;
        changes.Mode = 0x124;
        Nfs2AttrStat set = await nfs.SetAttributesAsync(wire, changes, Token);

        Assert.True(set.IsSuccess);
        Assert.Equal(0x124u, set.Attributes.Mode);
    }

    [Fact]
    public async Task ReadDirectory_ListsEntries()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1]);
        fileSystem.CreateFile(fileSystem.Root, "b.txt", [2]);
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);

        Nfs2ReadDirResult result = await nfs.ReadDirectoryAsync(ToWire(fileSystem.Root), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Eof);
        Assert.Equal(["a.txt", "b.txt"], result.Entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task FileSystemStatus_ReportsBlocks()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);

        Nfs2StatFsResult result = await nfs.FileSystemStatusAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(4096u, result.BlockSize);
        Assert.True(result.TotalBlocks > 0);
    }

    [Fact]
    public async Task SymbolicLink_ThenReadLink_RoundTrips()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);
        Nfs2Handle root = ToWire(fileSystem.Root);

        Nfs2StatResult symlink = await nfs.CreateSymbolicLinkAsync(root, "link", "/target", cancellationToken: Token);
        Assert.True(symlink.IsSuccess);

        Nfs2DirOpResult lookup = await nfs.LookupAsync(root, "link", Token);
        Nfs2ReadLinkResult readLink = await nfs.ReadSymbolicLinkAsync(lookup.Handle, Token);
        Assert.True(readLink.IsSuccess);
        Assert.Equal("/target", readLink.Target);
    }

    [Fact]
    public async Task GetAttributes_OnUnknownHandle_ReturnsStale()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs2Client nfs = await ConnectAsync(server);

        var unknown = new Nfs2Handle { Data = MakeUnknownHandle() };
        Nfs2AttrStat result = await nfs.GetAttributesAsync(unknown, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, result.Status);
    }

    [Fact]
    public async Task BothVersionsServedBySameProgram()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "shared.txt", Encoding.UTF8.GetBytes("hi"));
        await using var server = StartServer(fileSystem);

        Nfs2Client v2 = await ConnectAsync(server);
        Assert.True((await v2.LookupAsync(ToWire(fileSystem.Root), "shared.txt", Token)).IsSuccess);

        RpcClient rpc3 = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var v3 = new Nfs3Client(rpc3);
        var v3Root = new Protocol.V3.Nfs3Handle { Data = fileSystem.Root.ToArray() };
        Assert.True((await v3.LookupAsync(v3Root, "shared.txt", Token)).IsSuccess);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new NfsProgram(fileSystem));
        server.Start();
        return server;
    }

    private static async ValueTask<Nfs2Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nfs2Client(rpc);
    }

    private static Nfs2Handle ToWire(NfsFileHandle handle)
    {
        byte[] data = new byte[Nfs2.HandleSize];
        ReadOnlySpan<byte> source = handle.Span;
        data[0] = (byte)(source.Length >> 24);
        data[1] = (byte)(source.Length >> 16);
        data[2] = (byte)(source.Length >> 8);
        data[3] = (byte)source.Length;
        source.CopyTo(data.AsSpan(4));
        return new Nfs2Handle { Data = data };
    }

    private static byte[] MakeUnknownHandle()
    {
        byte[] data = new byte[Nfs2.HandleSize];
        data[3] = 8; // length = 8
        data[4] = 0xFF;
        data[5] = 0xFF;
        return data;
    }
}
