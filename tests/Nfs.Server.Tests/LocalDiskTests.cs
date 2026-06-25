using System.Buffers;
using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V3;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class LocalDiskTests : IDisposable
{
    private readonly string _root;

    public LocalDiskTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "nfs-localdisk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact]
    public async Task Root_ReportsDirectory()
    {
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(NfsFileType.Directory, result.Attributes.Type);
    }

    [Fact]
    public async Task PreexistingFile_IsVisibleAndReadable()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "readme.txt"), "hello world", Token);
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "readme.txt", Token);
        Assert.True(lookup.IsSuccess);

        Nfs3ReadResult read = await nfs.ReadAsync(lookup.Ok.Handle, 0, 1024, Token);
        Assert.True(read.IsSuccess);
        Assert.Equal("hello world", Encoding.UTF8.GetString(read.Ok.Data));
    }

    [Fact]
    public async Task BufferedRead_ExactSizeReturnsBytesAndEof()
    {
        byte[] payload = Encoding.UTF8.GetBytes("buffered exact read");
        await File.WriteAllBytesAsync(Path.Combine(_root, "buffered.bin"), payload, Token);
        var fileSystem = new LocalDiskFileSystem(_root);
        NfsFileHandle handle = await fileSystem.LookupAsync(fileSystem.Root, "buffered.bin", Token);
        var writer = new ArrayBufferWriter<byte>(payload.Length);

        NfsBufferedReadResult read = await fileSystem.ReadAsync(handle, 0, (uint)payload.Length, writer, Token);

        Assert.Equal((uint)payload.Length, read.Count);
        Assert.True(read.EndOfFile);
        Assert.Equal(payload, writer.WrittenSpan.ToArray());
    }

    [Fact]
    public async Task BufferedRead_PartialReadAtEofReturnsSuffix()
    {
        byte[] payload = Encoding.UTF8.GetBytes("0123456789");
        await File.WriteAllBytesAsync(Path.Combine(_root, "partial.bin"), payload, Token);
        var fileSystem = new LocalDiskFileSystem(_root);
        NfsFileHandle handle = await fileSystem.LookupAsync(fileSystem.Root, "partial.bin", Token);
        var writer = new ArrayBufferWriter<byte>();

        NfsBufferedReadResult read = await fileSystem.ReadAsync(handle, 7, 1024, writer, Token);

        Assert.Equal(3u, read.Count);
        Assert.True(read.EndOfFile);
        Assert.Equal("789", Encoding.UTF8.GetString(writer.WrittenSpan));
    }

    [Fact]
    public async Task Create_Write_Read_RoundTripsThroughDisk()
    {
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult create = await nfs.CreateAsync(root, "data.bin", Nfs3SetAttributes.None, Token);
        Assert.True(create.IsSuccess);
        Nfs3Handle file = create.Ok.Handle!.Value;

        byte[] payload = Encoding.UTF8.GetBytes("the quick brown fox");
        Nfs3WriteResult write = await nfs.WriteAsync(file, 0, payload, Nfs3StableHow.FileSync, Token);
        Assert.True(write.IsSuccess);
        Assert.Equal((uint)payload.Length, write.Ok.Count);

        Nfs3ReadResult read = await nfs.ReadAsync(file, 0, 1024, Token);
        Assert.True(read.IsSuccess);
        Assert.True(read.Ok.Eof);
        Assert.Equal(payload, read.Ok.Data);

        // The bytes actually landed on disk.
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(_root, "data.bin"), Token));
    }

    [Fact]
    public async Task MakeDirectory_ThenCreateChild_Works()
    {
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult mkdir = await nfs.MakeDirectoryAsync(root, "docs", Nfs3SetAttributes.None, Token);
        Assert.True(mkdir.IsSuccess);
        Assert.True(Directory.Exists(Path.Combine(_root, "docs")));

        Nfs3Handle docs = mkdir.Ok.Handle!.Value;
        Nfs3CreateResult child = await nfs.CreateAsync(docs, "a.txt", Nfs3SetAttributes.None, Token);
        Assert.True(child.IsSuccess);
        Assert.True(File.Exists(Path.Combine(_root, "docs", "a.txt")));
    }

    [Fact]
    public async Task ReadDirectory_ListsEntriesInOrder()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "b.txt"), "b", Token);
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "a", Token);
        Directory.CreateDirectory(Path.Combine(_root, "sub"));

        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadDirResult result = await nfs.ReadDirectoryAsync(ToWire(fileSystem.Root), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Ok.Eof);
        string[] names = result.Ok.Entries.Select(e => e.Name).ToArray();
        Assert.Equal(["a.txt", "b.txt", "sub"], names);
    }

    [Fact]
    public async Task Remove_DeletesFileFromDisk()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "f"), "x", Token);
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3WccResult remove = await nfs.RemoveAsync(root, "f", Token);
        Assert.True(remove.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_root, "f")));
    }

    [Fact]
    public async Task RemoveDirectory_NonEmpty_ReturnsNotEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_root, "d"));
        await File.WriteAllTextAsync(Path.Combine(_root, "d", "child"), "x", Token);
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3WccResult rmdir = await nfs.RemoveDirectoryAsync(ToWire(fileSystem.Root), "d", Token);

        Assert.False(rmdir.IsSuccess);
        Assert.Equal(NfsStatus.DirectoryNotEmpty, rmdir.Status);
    }

    [Fact]
    public async Task Lookup_TraversalName_IsRejected()
    {
        var fileSystem = new LocalDiskFileSystem(_root);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult result = await nfs.LookupAsync(ToWire(fileSystem.Root), "..", Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.InvalidArgument, result.Status);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs3Program(fileSystem));
        server.Start();
        return server;
    }

    private static async ValueTask<Nfs3Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nfs3Client(rpc);
    }

    private static Nfs3Handle ToWire(NfsFileHandle handle) => new() { Data = handle.ToArray() };
}
