using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V3;
using Nfs.Rpc;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Nfs3ServerTests
{
    [Fact]
    public async Task Root_ReportsDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(NfsFileType.Directory, result.Attributes.Type);
    }

    [Fact]
    public async Task Lookup_ThenGetAttributes_OnAFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "readme.txt", Encoding.UTF8.GetBytes("hello world"));

        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult lookup = await nfs.LookupAsync(ToWire(fileSystem.Root), "readme.txt", Token);
        Assert.True(lookup.IsSuccess);

        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(lookup.Ok.Handle, Token);
        Assert.True(attributes.IsSuccess);
        Assert.Equal(NfsFileType.Regular, attributes.Attributes.Type);
        Assert.Equal(11ul, attributes.Attributes.Size); // "hello world"
    }

    [Fact]
    public async Task Lookup_OnSubdirectory_Works()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle docs = fileSystem.CreateDirectory(fileSystem.Root, "docs");
        fileSystem.CreateFile(docs, "a.txt", [1, 2, 3]);

        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult docsLookup = await nfs.LookupAsync(ToWire(fileSystem.Root), "docs", Token);
        Assert.True(docsLookup.IsSuccess);

        Nfs3LookupResult fileLookup = await nfs.LookupAsync(docsLookup.Ok.Handle, "a.txt", Token);
        Assert.True(fileLookup.IsSuccess);

        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(fileLookup.Ok.Handle, Token);
        Assert.Equal(3ul, attributes.Attributes.Size);
    }

    [Fact]
    public async Task GetAttributes_OnUnknownHandle_ReturnsStale()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(
            new Nfs3Handle { Data = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF] }, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, result.Status);
    }

    [Fact]
    public async Task Lookup_MissingName_ReturnsNoEntry()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult result = await nfs.LookupAsync(ToWire(fileSystem.Root), "missing", Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.NoEntry, result.Status);
    }

    [Fact]
    public async Task Write_ThenRead_RoundTripsContent()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "data.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle wire = ToWire(file);

        byte[] payload = Encoding.UTF8.GetBytes("the quick brown fox");
        Nfs3WriteResult write = await nfs.WriteAsync(wire, 0, payload, Nfs3StableHow.FileSync, Token);
        Assert.True(write.IsSuccess);
        Assert.Equal((uint)payload.Length, write.Ok.Count);
        Assert.Equal(Nfs3StableHow.FileSync, write.Ok.Committed);

        Nfs3ReadResult read = await nfs.ReadAsync(wire, 0, 1024, Token);
        Assert.True(read.IsSuccess);
        Assert.True(read.Ok.Eof);
        Assert.Equal(payload, read.Ok.Data);
    }

    [Fact]
    public async Task Write_LargePayload_RoundTripsContent()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "large.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle wire = ToWire(file);
        byte[] payload = new byte[1024 * 1024];
        new Random(42).NextBytes(payload);

        Nfs3WriteResult write = await nfs.WriteAsync(wire, 0, payload, Nfs3StableHow.FileSync, Token);
        Nfs3ReadResult read = await nfs.ReadAsync(wire, 0, Nfs3.MaxReadSize, Token);

        Assert.True(write.IsSuccess);
        Assert.Equal((uint)payload.Length, write.Ok.Count);
        Assert.True(read.IsSuccess);
        Assert.True(read.Ok.Eof);
        Assert.Equal(payload, read.Ok.Data);
    }

    [Fact]
    public async Task Read_WithOffsetAndCount_ReturnsSlice()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "data.bin", Encoding.UTF8.GetBytes("0123456789"));
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadResult read = await nfs.ReadAsync(ToWire(file), offset: 3, count: 4, Token);

        Assert.True(read.IsSuccess);
        Assert.False(read.Ok.Eof);
        Assert.Equal(Encoding.UTF8.GetBytes("3456"), read.Ok.Data);
    }

    [Fact]
    public async Task Read_PastEndOfFile_ReturnsEmptyAtEof()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadResult read = await nfs.ReadAsync(ToWire(file), offset: 10, count: 4, Token);

        Assert.True(read.IsSuccess);
        Assert.True(read.Ok.Eof);
        Assert.Empty(read.Ok.Data);
    }

    [Fact]
    public async Task Write_ExtendsTheFile()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle wire = ToWire(file);

        await nfs.WriteAsync(wire, offset: 5, [9], Nfs3StableHow.FileSync, Token);

        Nfs3ReadResult read = await nfs.ReadAsync(wire, 0, 100, Token);
        Assert.Equal(6, read.Ok.Data.Length);
        Assert.Equal(9, read.Ok.Data[5]);
    }

    [Fact]
    public async Task Write_ToADirectory_ReturnsIsDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3WriteResult write = await nfs.WriteAsync(ToWire(fileSystem.Root), 0, [1], Nfs3StableHow.FileSync, Token);

        Assert.False(write.IsSuccess);
        Assert.Equal(NfsStatus.IsDirectory, write.Status);
    }

    [Fact]
    public async Task Access_OnAFile_GrantsRequestedBits()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3AccessResult result = await nfs.AccessAsync(ToWire(file), Nfs3Access.Read | Nfs3Access.Modify, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(Nfs3Access.Read | Nfs3Access.Modify, result.Ok.Access);
    }

    [Fact]
    public async Task Access_OnUnknownHandle_ReturnsStale()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3AccessResult result = await nfs.AccessAsync(
            new Nfs3Handle { Data = [0xAB, 0xCD] }, Nfs3Access.Read, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, result.Status);
    }

    [Fact]
    public async Task FileSystemInfo_ReportsServerLimits()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3FsInfoResult result = await nfs.FileSystemInfoAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal((uint)Nfs3.MaxReadSize, result.Ok.ReadMax);
        Assert.Equal((uint)Nfs3.MaxWriteSize, result.Ok.WriteMax);
        Assert.NotEqual(0u, result.Ok.Properties & Nfs3FsProperties.Homogeneous);
        Assert.NotNull(result.Ok.Attributes);
    }

    [Fact]
    public async Task Create_ThenLookup_FindsNewFile()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult create = await nfs.CreateAsync(root, "new.txt", Nfs3SetAttributes.None, Token);
        Assert.True(create.IsSuccess);
        Assert.NotNull(create.Ok.Handle);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "new.txt", Token);
        Assert.True(lookup.IsSuccess);
    }

    [Fact]
    public async Task Create_Duplicate_ReturnsAlreadyExists()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "f", []);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3CreateResult create = await nfs.CreateAsync(ToWire(fileSystem.Root), "f", Nfs3SetAttributes.None, Token);

        Assert.False(create.IsSuccess);
        Assert.Equal(NfsStatus.AlreadyExists, create.Status);
    }

    [Fact]
    public async Task CreatedFile_CanBeWrittenAndReadBack()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3CreateResult create = await nfs.CreateAsync(ToWire(fileSystem.Root), "life.txt", Nfs3SetAttributes.None, Token);
        Nfs3Handle file = create.Ok.Handle!.Value;

        await nfs.WriteAsync(file, 0, Encoding.UTF8.GetBytes("content"), Nfs3StableHow.FileSync, Token);
        Nfs3ReadResult read = await nfs.ReadAsync(file, 0, 100, Token);

        Assert.Equal("content", Encoding.UTF8.GetString(read.Ok.Data));
    }

    [Fact]
    public async Task MakeDirectory_CreatesADirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult mkdir = await nfs.MakeDirectoryAsync(root, "sub", Nfs3SetAttributes.None, Token);
        Assert.True(mkdir.IsSuccess);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "sub", Token);
        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(lookup.Ok.Handle, Token);
        Assert.Equal(NfsFileType.Directory, attributes.Attributes.Type);
    }

    [Fact]
    public async Task Remove_DeletesTheFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "f", [1]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3WccResult remove = await nfs.RemoveAsync(root, "f", Token);
        Assert.True(remove.IsSuccess);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "f", Token);
        Assert.Equal(NfsStatus.NoEntry, lookup.Status);
    }

    [Fact]
    public async Task RemoveDirectory_NonEmpty_ReturnsNotEmpty()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle directory = fileSystem.CreateDirectory(fileSystem.Root, "d");
        fileSystem.CreateFile(directory, "child", []);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3WccResult rmdir = await nfs.RemoveDirectoryAsync(ToWire(fileSystem.Root), "d", Token);

        Assert.False(rmdir.IsSuccess);
        Assert.Equal(NfsStatus.DirectoryNotEmpty, rmdir.Status);
    }

    [Fact]
    public async Task RemoveDirectory_Empty_Succeeds()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory(fileSystem.Root, "d");
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3WccResult rmdir = await nfs.RemoveDirectoryAsync(ToWire(fileSystem.Root), "d", Token);

        Assert.True(rmdir.IsSuccess);
    }

    [Fact]
    public async Task ReadDirectory_ListsAllEntries()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1]);
        fileSystem.CreateFile(fileSystem.Root, "b.txt", [2]);
        fileSystem.CreateDirectory(fileSystem.Root, "sub");
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadDirResult result = await nfs.ReadDirectoryAsync(ToWire(fileSystem.Root), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Ok.Eof);
        string[] names = result.Ok.Entries.Select(e => e.Name).ToArray();
        Assert.Equal(["a.txt", "b.txt", "sub"], names);
    }

    [Fact]
    public async Task ReadDirectory_EmptyDirectory_ReturnsNoEntries()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle directory = fileSystem.CreateDirectory(fileSystem.Root, "empty");
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadDirResult result = await nfs.ReadDirectoryAsync(ToWire(directory), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Ok.Entries);
        Assert.True(result.Ok.Eof);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SetAttributes_ChangesMode()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle wire = ToWire(file);

        Nfs3WccResult set = await nfs.SetAttributesAsync(wire, new Nfs3SetAttributes { Mode = 0x124 }, cancellationToken: Token);
        Assert.True(set.IsSuccess);

        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(wire, Token);
        Assert.Equal(0x124u, attributes.Attributes.Mode);
    }

    [Fact]
    public async Task SetAttributes_TruncatesAndExtendsFile()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3, 4, 5]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle wire = ToWire(file);

        await nfs.SetAttributesAsync(wire, new Nfs3SetAttributes { Size = 2 }, cancellationToken: Token);
        Nfs3GetAttrResult shrunk = await nfs.GetAttributesAsync(wire, Token);
        Assert.Equal(2ul, shrunk.Attributes.Size);

        await nfs.SetAttributesAsync(wire, new Nfs3SetAttributes { Size = 8 }, cancellationToken: Token);
        Nfs3ReadResult read = await nfs.ReadAsync(wire, 0, 100, Token);
        Assert.Equal(8, read.Ok.Data.Length);
        Assert.Equal(new byte[] { 1, 2, 0, 0, 0, 0, 0, 0 }, read.Ok.Data);
    }

    [Fact]
    public async Task Rename_MovesFileBetweenDirectories()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle source = fileSystem.CreateDirectory(fileSystem.Root, "src");
        NfsFileHandle destination = fileSystem.CreateDirectory(fileSystem.Root, "dst");
        fileSystem.CreateFile(source, "a.txt", [9]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3RenameResult rename = await nfs.RenameAsync(ToWire(source), "a.txt", ToWire(destination), "b.txt", Token);
        Assert.True(rename.IsSuccess);

        Assert.Equal(NfsStatus.NoEntry, (await nfs.LookupAsync(ToWire(source), "a.txt", Token)).Status);
        Assert.True((await nfs.LookupAsync(ToWire(destination), "b.txt", Token)).IsSuccess);
    }

    [Fact]
    public async Task Rename_OntoExistingFile_Replaces()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "from", [1]);
        fileSystem.CreateFile(fileSystem.Root, "to", [2]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3RenameResult rename = await nfs.RenameAsync(root, "from", root, "to", Token);
        Assert.True(rename.IsSuccess);
        Assert.Equal(NfsStatus.NoEntry, (await nfs.LookupAsync(root, "from", Token)).Status);
    }

    [Fact]
    public async Task SymbolicLink_ThenReadLink_RoundTrips()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult symlink = await nfs.CreateSymbolicLinkAsync(root, "link", "/target/path", cancellationToken: Token);
        Assert.True(symlink.IsSuccess);

        Nfs3ReadLinkResult readLink = await nfs.ReadSymbolicLinkAsync(symlink.Ok.Handle!.Value, Token);
        Assert.True(readLink.IsSuccess);
        Assert.Equal("/target/path", readLink.Target);
    }

    [Fact]
    public async Task Link_CreatesSecondNameForFile()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "original", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3LinkResult link = await nfs.LinkAsync(ToWire(file), root, "hardlink", Token);
        Assert.True(link.IsSuccess);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "hardlink", Token);
        Assert.True(lookup.IsSuccess);
        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(lookup.Ok.Handle, Token);
        Assert.Equal(2u, attributes.Attributes.LinkCount);
    }

    [Fact]
    public async Task ReadDirectoryPlus_ReturnsEntriesWithAttributesAndHandles()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "a.txt", [1, 2, 3]);
        fileSystem.CreateDirectory(fileSystem.Root, "sub");
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3ReadDirPlusResult result = await nfs.ReadDirectoryPlusAsync(ToWire(fileSystem.Root), cancellationToken: Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.Ok.Eof);
        Assert.Equal(["a.txt", "sub"], result.Ok.Entries.Select(e => e.Name).ToArray());
        Nfs3DirEntryPlus file = result.Ok.Entries.First(e => e.Name == "a.txt");
        Assert.NotNull(file.Attributes);
        Assert.Equal(NfsFileType.Regular, file.Attributes!.Value.Type);
        Assert.NotNull(file.Handle);
    }

    [Fact]
    public async Task FileSystemStatus_ReportsCapacity()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3FsStatResult result = await nfs.FileSystemStatusAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.True(result.TotalBytes > 0);
        Assert.True(result.FreeBytes <= result.TotalBytes);
    }

    [Fact]
    public async Task PathConfiguration_ReportsLimits()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3PathConfResult result = await nfs.PathConfigurationAsync(ToWire(fileSystem.Root), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal((uint)Nfs3.MaxNameLength, result.NameMax);
        Assert.True(result.CasePreserving);
    }

    [Fact]
    public async Task Commit_OnFile_Succeeds()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle file = fileSystem.CreateFile(fileSystem.Root, "f", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3CommitResult result = await nfs.CommitAsync(ToWire(file), 0, 0, Token);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Verifier);
        Assert.Equal(8, result.Verifier!.Length);
    }

    [Fact]
    public async Task Commit_OnUnknownHandle_ReturnsStale()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3CommitResult result = await nfs.CommitAsync(
            new Nfs3Handle { Data = [0xDE, 0xAD, 0xBE, 0xEF, 0, 0, 0, 0] }, 0, 0, Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, result.Status);
    }

    [Fact]
    public async Task MakeNode_Fifo_CreatesFifo()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);
        Nfs3Handle root = ToWire(fileSystem.Root);

        Nfs3CreateResult create = await nfs.MakeNodeAsync(root, "pipe", Nfs3MknodData.Fifo(), Token);
        Assert.True(create.IsSuccess);

        Nfs3LookupResult lookup = await nfs.LookupAsync(root, "pipe", Token);
        Nfs3GetAttrResult attributes = await nfs.GetAttributesAsync(lookup.Ok.Handle, Token);
        Assert.Equal(NfsFileType.Fifo, attributes.Attributes.Type);
    }

    [Fact]
    public async Task MakeNode_BlockDevice_ReturnsNotSupported()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3CreateResult create = await nfs.MakeNodeAsync(
            ToWire(fileSystem.Root),
            "block",
            Nfs3MknodData.BlockDevice(spec: new Nfs3SpecData { Major = 1, Minor = 2 }),
            Token);

        Assert.False(create.IsSuccess);
        Assert.Equal(NfsStatus.NotSupported, create.Status);
    }

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
