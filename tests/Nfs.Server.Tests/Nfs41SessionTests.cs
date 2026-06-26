using System.Buffers;
using System.Net;
using System.Text;

using Nfs.Abstractions;
using Nfs.Client;
using Nfs.Protocol.V4;
using Nfs.Rpc;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Server.Tests;

public sealed class Nfs41SessionTests
{
    [Fact]
    public async Task ExchangeId_CreateSession_ThenSequencedGetAttr()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        (ulong clientId, byte[] sessionId) = await EstablishSessionAsync(nfs);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "seq-getattr",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, sequenceId: 1, slot: 0, cacheThis: true),
                new Nfs4PutRootFhOp(),
                new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Type) },
            ],
            Token);

        Assert.Equal(Nfs4Status.Ok, result.Status);
        Assert.IsType<Nfs4SequenceResult>(result.Operations[0]);
        var attr = Assert.IsType<Nfs4GetAttrResult>(result.Operations[^1]);
        Assert.Equal(Nfs4FileType.Directory, Nfs4FileAttributes.Decode(attr.Attributes).Type);
        Assert.True(clientId > 0);
    }

    [Fact]
    public async Task RetransmittedSequence_ReplaysCachedReply()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "x", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);

        // First use of slot 0 / sequence 1: look up a file and return its handle, cached.
        Nfs4CompoundResult first = await nfs.CompoundAsync(
            "seq1",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, sequenceId: 1, slot: 0, cacheThis: true),
                new Nfs4PutRootFhOp(),
                new Nfs4LookupOp { Name = "x" },
                new Nfs4GetFhOp(),
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, first.Status);
        byte[] firstHandle = Assert.IsType<Nfs4GetFhResult>(first.Operations[^1]).Handle.Data;

        // Retransmit slot 0 / sequence 1 with fewer ops: the server must replay the cached reply,
        // so the decoded result still contains the original four operations and the same handle.
        Nfs4CompoundResult replay = await nfs.CompoundAsync(
            "seq1-retry",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, sequenceId: 1, slot: 0, cacheThis: true)],
            Token);

        Assert.Equal(4, replay.Operations.Count);
        Assert.Equal(firstHandle, Assert.IsType<Nfs4GetFhResult>(replay.Operations[^1]).Handle.Data);
    }

    [Fact]
    public async Task MisorderedSequence_ReturnsError()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);

        // Skip ahead from the initial sequence 0 to 5 (a gap): the slot rejects it.
        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "seq-gap",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, sequenceId: 5, slot: 0, cacheThis: false), new Nfs4PutRootFhOp()],
            Token);

        Assert.Equal(Nfs4Status.SequenceMisordered, result.Status);
    }

    [Fact]
    public async Task Sequence_OnUnknownSession_ReturnsBadSession()
    {
        var fileSystem = new InMemoryFileSystem();
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "seq-bad",
            Nfs4.MinorVersion1,
            [Sequence(new byte[Nfs4.SessionIdSize], sequenceId: 1, slot: 0, cacheThis: false)],
            Token);

        Assert.Equal(Nfs4Status.BadSession, result.Status);
    }

    [Fact]
    public async Task Allocate_ExtendsFile_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "a.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4CompoundResult alloc = await nfs.CompoundAsync(
            "allocate",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 1, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4AllocateOp { Offset = 0, Length = 100 }],
            Token);
        Assert.Equal(Nfs4Status.Ok, alloc.Status);

        Nfs4CompoundResult get = await nfs.CompoundAsync(
            "getsize",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 2, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4GetAttrOp { Request = Nfs4Bitmap.Of(Nfs4AttributeId.Size) }],
            Token);
        var attr = Assert.IsType<Nfs4GetAttrResult>(get.Operations[^1]);
        Assert.Equal(100ul, Nfs4FileAttributes.Decode(attr.Attributes).Size);
    }

    [Fact]
    public async Task Seek_FindsDataAndHole_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "s.bin", new byte[10]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4CompoundResult data = await nfs.CompoundAsync(
            "seek-data",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 1, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4SeekOp { Offset = 0, What = Nfs4ContentType.Data }],
            Token);
        var dataResult = Assert.IsType<Nfs4SeekResult>(data.Operations[^1]);
        Assert.False(dataResult.Eof);
        Assert.Equal(0ul, dataResult.Offset);

        Nfs4CompoundResult hole = await nfs.CompoundAsync(
            "seek-hole",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 2, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4SeekOp { Offset = 0, What = Nfs4ContentType.Hole }],
            Token);
        Assert.Equal(10ul, Assert.IsType<Nfs4SeekResult>(hole.Operations[^1]).Offset);
    }

    [Fact]
    public async Task Deallocate_ZeroesRange_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "d.bin", [1, 2, 3, 4, 5]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4CompoundResult dealloc = await nfs.CompoundAsync(
            "deallocate",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 1, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4DeallocateOp { Offset = 1, Length = 2 }],
            Token);
        Assert.Equal(Nfs4Status.Ok, dealloc.Status);

        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "read",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 2, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4ReadOp { Offset = 0, Count = 10 }],
            Token);
        Assert.Equal(new byte[] { 1, 0, 0, 4, 5 }, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);
    }

    [Fact]
    public async Task Copy_CopiesBetweenSavedAndCurrentFile_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle sourceBacking = fileSystem.CreateFile(fileSystem.Root, "source.bin", [1, 2, 3, 4, 5]);
        NfsFileHandle destinationBacking = fileSystem.CreateFile(fileSystem.Root, "destination.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var source = new Nfs4Handle { Data = sourceBacking.ToArray() };
        var destination = new Nfs4Handle { Data = destinationBacking.ToArray() };

        Nfs4CompoundResult copy = await nfs.CompoundAsync(
            "copy",
            Nfs4.MinorVersion2,
            [
                Sequence(sessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = source },
                new Nfs4SaveFhOp(),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4CopyOp { SourceOffset = 1, DestinationOffset = 0, Count = 3, Synchronous = true },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, copy.Status);
        Assert.Equal(3ul, Assert.IsType<Nfs4CopyResult>(copy.Operations[^1]).Response.Count);

        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "read-copy",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 2, 0, false), new Nfs4PutFhOp { Handle = destination }, new Nfs4ReadOp { Offset = 0, Count = 10 }],
            Token);
        Assert.Equal(new byte[] { 2, 3, 4 }, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);
    }

    [Fact]
    public async Task InterServerCopy_PullsFromSourceOverRpc_Version42()
    {
        var sourceFileSystem = new InMemoryFileSystem();
        var destinationFileSystem = new InMemoryFileSystem();
        NfsFileHandle sourceBacking = sourceFileSystem.CreateFile(
            sourceFileSystem.Root,
            "remote-source.bin",
            [1, 2, 3, 4, 5, 6]);
        NfsFileHandle destinationBacking = destinationFileSystem.CreateFile(
            destinationFileSystem.Root,
            "remote-destination.bin",
            []);
        await using var sourceServer = StartServer(sourceFileSystem);
        await using var destinationServer = StartServer(destinationFileSystem);
        Nfs4Client sourceClient = await ConnectAsync(sourceServer);
        Nfs4Client destinationClient = await ConnectAsync(destinationServer);
        (_, byte[] sourceSessionId) = await EstablishSessionAsync(sourceClient, "remote-copy-source", 0);
        (_, byte[] destinationSessionId) = await EstablishSessionAsync(destinationClient, "remote-copy-destination", 0);
        var source = new Nfs4Handle { Data = sourceBacking.ToArray() };
        var destination = new Nfs4Handle { Data = destinationBacking.ToArray() };

        Nfs4CompoundResult notify = await sourceClient.CompoundAsync(
            "remote-copy-notify",
            Nfs4.MinorVersion2,
            [
                Sequence(sourceSessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = source },
                new Nfs4CopyNotifyOp { Destination = new Nfs4NetLocation() },
            ],
            Token);
        var notifyResult = Assert.IsType<Nfs4CopyNotifyResult>(notify.Operations[^1]);
        Assert.Equal(Nfs4Status.Ok, notify.Status);
        Nfs4NetLocation sourceLocation = Assert.Single(notifyResult.SourceLocations);
        Assert.Equal(Nfs4NetLocationType.Url, sourceLocation.Type);
        var sourceUri = new Uri(sourceLocation.Value);
        Assert.Equal(sourceServer.LocalEndPoint.Port, sourceUri.Port);
        Assert.NotEqual(destinationServer.LocalEndPoint.Port, sourceUri.Port);

        Nfs4CompoundResult copy = await destinationClient.CompoundAsync(
            "remote-copy",
            Nfs4.MinorVersion2,
            [
                Sequence(destinationSessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4CopyOp
                {
                    SourceStateId = notifyResult.StateId,
                    SourceOffset = 2,
                    DestinationOffset = 0,
                    Count = 3,
                    Synchronous = true,
                    SourceServers = { sourceLocation },
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, copy.Status);
        Assert.Equal(3ul, Assert.IsType<Nfs4CopyResult>(copy.Operations[^1]).Response.Count);

        Nfs4CompoundResult read = await destinationClient.CompoundAsync(
            "read-remote-copy",
            Nfs4.MinorVersion2,
            [
                Sequence(destinationSessionId, 2, 0, false),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4ReadOp { Offset = 0, Count = 10 },
            ],
            Token);
        Assert.Equal(new byte[] { 3, 4, 5 }, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);
    }

    [Fact]
    public async Task AsyncCopyNotifyCopy_FiresCbOffloadAndCopiesData_Version42()
    {
        const uint callbackProgram = 0x40000124;
        var sourceFileSystem = new InMemoryFileSystem();
        var destinationFileSystem = new InMemoryFileSystem();
        NfsFileHandle sourceBacking = sourceFileSystem.CreateFile(
            sourceFileSystem.Root,
            "async-source.bin",
            [10, 11, 12, 13, 14]);
        NfsFileHandle destinationBacking = destinationFileSystem.CreateFile(
            destinationFileSystem.Root,
            "async-destination.bin",
            []);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start(callbackProgram);
        var transport = new TcpBackChannelTransport(callback.EndPoint);
        await using var sourceServer = StartServer(sourceFileSystem);
        await using var destinationServer = StartServer(destinationFileSystem, transport);
        Nfs4Client sourceClient = await ConnectAsync(sourceServer);
        Nfs4Client destinationClient = await ConnectAsync(destinationServer);
        (_, byte[] sourceSessionId) = await EstablishSessionAsync(sourceClient, "async-copy-source", 0);
        (_, byte[] destinationSessionId) = await EstablishSessionAsync(
            destinationClient,
            "async-copy-destination",
            callbackProgram);
        var source = new Nfs4Handle { Data = sourceBacking.ToArray() };
        var destination = new Nfs4Handle { Data = destinationBacking.ToArray() };

        Nfs4CompoundResult notify = await sourceClient.CompoundAsync(
            "copy-notify",
            Nfs4.MinorVersion2,
            [
                Sequence(sourceSessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = source },
                new Nfs4CopyNotifyOp { Destination = new Nfs4NetLocation() },
            ],
            Token);
        var notifyResult = Assert.IsType<Nfs4CopyNotifyResult>(notify.Operations[^1]);
        Assert.Equal(Nfs4Status.Ok, notify.Status);
        Assert.Single(notifyResult.SourceLocations);

        Nfs4CompoundResult copy = await destinationClient.CompoundAsync(
            "async-copy",
            Nfs4.MinorVersion2,
            [
                Sequence(destinationSessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4CopyOp
                {
                    SourceStateId = notifyResult.StateId,
                    SourceOffset = 1,
                    DestinationOffset = 0,
                    Count = 3,
                    Synchronous = false,
                    SourceServers = { notifyResult.SourceLocations[0] },
                },
            ],
            Token);
        var copyResult = Assert.IsType<Nfs4CopyResult>(copy.Operations[^1]);
        Assert.False(copyResult.Synchronous);
        Nfs4StateId callbackId = copyResult.Response.CallbackId!.Value;

        Nfs4CompoundResult status = await destinationClient.CompoundAsync(
            "offload-status",
            Nfs4.MinorVersion2,
            [Sequence(destinationSessionId, 2, 0, false), new Nfs4OffloadStatusOp { StateId = callbackId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, status.Status);
        Assert.True(Assert.IsType<Nfs4OffloadStatusResult>(status.Operations[^1]).Count <= 3);

        Nfs4CallbackOffloadOp offload = await callback.WaitForOffloadAsync(Token);
        Assert.Equal(callbackId.Other, offload.StateId.Other);
        Assert.Equal(Nfs4Status.Ok, offload.Status);
        Assert.Equal(3ul, offload.Response.Count);
        Assert.NotNull(transport.LastCompound);
        Assert.IsType<Nfs4CallbackSequenceOp>(transport.LastCompound!.Operations[0]);
        Assert.IsType<Nfs4CallbackOffloadOp>(transport.LastCompound.Operations[1]);

        Nfs4CompoundResult read = await destinationClient.CompoundAsync(
            "read-async-copy",
            Nfs4.MinorVersion2,
            [
                Sequence(destinationSessionId, 3, 0, false),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4ReadOp { Offset = 0, Count = 10 },
            ],
            Token);
        Assert.Equal(new byte[] { 11, 12, 13 }, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);

        Nfs4CompoundResult cancel = await destinationClient.CompoundAsync(
            "offload-cancel",
            Nfs4.MinorVersion2,
            [Sequence(destinationSessionId, 4, 0, false), new Nfs4OffloadCancelOp { StateId = callbackId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, cancel.Status);
    }

    [Fact]
    public async Task ReadPlus_ReturnsSingleDataSegment_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "rp.bin", [9, 8, 7]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "read-plus",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 1, 0, false), new Nfs4PutFhOp { Handle = file }, new Nfs4ReadPlusOp { Offset = 1, Count = 2 }],
            Token);

        var readPlus = Assert.IsType<Nfs4ReadPlusResult>(result.Operations[^1]);
        Assert.True(readPlus.Eof);
        var data = Assert.IsType<Nfs4ReadPlusData>(Assert.Single(readPlus.Contents));
        Assert.Equal(1ul, data.Offset);
        Assert.Equal(new byte[] { 8, 7 }, data.Data);
    }

    [Fact]
    public async Task Clone_EmulatesCopyBetweenSavedAndCurrentFile_Version42()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle sourceBacking = fileSystem.CreateFile(fileSystem.Root, "clone-source.bin", [4, 5, 6]);
        NfsFileHandle destinationBacking = fileSystem.CreateFile(fileSystem.Root, "clone-destination.bin", []);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var source = new Nfs4Handle { Data = sourceBacking.ToArray() };
        var destination = new Nfs4Handle { Data = destinationBacking.ToArray() };

        Nfs4CompoundResult clone = await nfs.CompoundAsync(
            "clone",
            Nfs4.MinorVersion2,
            [
                Sequence(sessionId, 1, 0, false),
                new Nfs4PutFhOp { Handle = source },
                new Nfs4SaveFhOp(),
                new Nfs4PutFhOp { Handle = destination },
                new Nfs4CloneOp { SourceOffset = 0, DestinationOffset = 0, Count = 3 },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, clone.Status);

        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "read-clone",
            Nfs4.MinorVersion2,
            [Sequence(sessionId, 2, 0, false), new Nfs4PutFhOp { Handle = destination }, new Nfs4ReadOp { Offset = 0, Count = 10 }],
            Token);
        Assert.Equal(new byte[] { 4, 5, 6 }, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);
    }

    [Fact]
    public async Task PnfsFilesLayout_LoopbackDevice_UsesReturnedFileHandleForIo()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "pnfs.bin", [1, 2, 3]);
        await using var server = StartServer(fileSystem);
        Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4CompoundResult deviceInfo = await nfs.CompoundAsync(
            "pnfs-device",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, 1, 0, false), new Nfs4GetDeviceInfoOp()],
            Token);
        Assert.Equal(Nfs4Status.Ok, deviceInfo.Status);
        var device = Assert.IsType<Nfs4GetDeviceInfoResult>(deviceInfo.Operations[^1]);
        Assert.Equal(Nfs4LayoutType.Files, device.DeviceAddress.LayoutType);
        Nfs4FileLayoutDataServerAddress address = Nfs4FileLayoutDataServerAddress.Decode(device.DeviceAddress.Body);
        Nfs4NetAddress ds = Assert.Single(Assert.Single(address.MultipathDataServers));
        Assert.Equal("tcp", ds.NetId);

        Nfs4CompoundResult layoutGet = await nfs.CompoundAsync(
            "pnfs-layout",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, 2, 0, false),
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LayoutGetOp { Iomode = Nfs4LayoutIomode.ReadWrite },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, layoutGet.Status);
        var layoutResult = Assert.IsType<Nfs4LayoutGetResult>(layoutGet.Operations[^1]);
        Nfs4Layout layout = Assert.Single(layoutResult.Layouts);
        Assert.Equal(0ul, layout.Offset);
        Assert.Equal(ulong.MaxValue, layout.Length);
        Nfs4FileLayout filesLayout = Nfs4FileLayout.Decode(layout.Content.Body);
        Nfs4Handle dataHandle = Assert.Single(filesLayout.FileHandles);
        Assert.Equal(file.Data, dataHandle.Data);

        byte[] payload = [9, 8, 7, 6];
        Nfs4CompoundResult write = await nfs.CompoundAsync(
            "pnfs-ds-write",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, 3, 0, false), new Nfs4PutFhOp { Handle = dataHandle }, new Nfs4WriteOp { Data = payload }],
            Token);
        Assert.Equal((uint)payload.Length, Assert.IsType<Nfs4WriteResult>(write.Operations[^1]).Count);

        Nfs4CompoundResult read = await nfs.CompoundAsync(
            "pnfs-ds-read",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, 4, 0, false), new Nfs4PutFhOp { Handle = dataHandle }, new Nfs4ReadOp { Count = 16 }],
            Token);
        Assert.Equal(payload, Assert.IsType<Nfs4ReadResult>(read.Operations[^1]).Data);

        Nfs4CompoundResult commit = await nfs.CompoundAsync(
            "pnfs-commit",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, 5, 0, false),
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LayoutCommitOp { Length = ulong.MaxValue, StateId = layoutResult.StateId },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, commit.Status);
        Assert.Null(Assert.IsType<Nfs4LayoutCommitResult>(commit.Operations[^1]).NewSize);

        Nfs4CompoundResult layoutReturn = await nfs.CompoundAsync(
            "pnfs-return",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, 6, 0, false),
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LayoutReturnOp { StateId = layoutResult.StateId },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, layoutReturn.Status);
        Assert.Null(Assert.IsType<Nfs4LayoutReturnResult>(layoutReturn.Operations[^1]).StateId);
    }

    [Fact]
    public async Task PnfsFilesLayout_MultiDataServerDevice_StripesIoAcrossDataServers()
    {
        const uint stripeUnit = 4096;
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "striped.bin", []);
        var dsPrograms = new[]
        {
            new CountingNfs4Program(fileSystem),
            new CountingNfs4Program(fileSystem),
        };
        await using var ds0 = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), dsPrograms[0]);
        await using var ds1 = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), dsPrograms[1]);
        ds0.Start();
        ds1.Start();

        var pnfsOptions = new Nfs4PnfsOptions(
            [UniversalAddress(ds0.LocalEndPoint), UniversalAddress(ds1.LocalEndPoint)],
            stripeUnit);
        await using var mds = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs4Program(fileSystem, pnfsOptions: pnfsOptions));
        mds.Start();

        await using Nfs4Client mdsClient = await ConnectAsync(mds);
        (_, byte[] sessionId) = await EstablishSessionAsync(mdsClient, "pnfs-striped", 0);
        var pnfsSession = new Nfs4PnfsSession(sessionId);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        Nfs4PnfsDevice device = await mdsClient.GetDeviceInfoAsync(Nfs4Pnfs.DefaultDeviceId, pnfsSession, Token);
        Nfs4FileLayoutDataServerAddress address = device.FilesAddress;
        Assert.Equal([0u, 1], address.StripeIndices);
        Assert.Equal(2, address.MultipathDataServers.Length);
        Assert.Equal(UniversalAddress(ds0.LocalEndPoint), address.MultipathDataServers[0][0].Uaddr);
        Assert.Equal(UniversalAddress(ds1.LocalEndPoint), address.MultipathDataServers[1][0].Uaddr);

        Nfs4PnfsLayout? layout = await mdsClient.LayoutGetAsync(
            file,
            iomode: Nfs4LayoutIomode.ReadWrite,
            session: pnfsSession,
            cancellationToken: Token);
        Assert.NotNull(layout);
        Nfs4FileLayout filesLayout = layout.FilesLayout;
        Assert.Equal(stripeUnit, filesLayout.StripeUnit);
        Assert.Equal(Nfs4Pnfs.FileLayoutUtilDense, filesLayout.Util & Nfs4Pnfs.FileLayoutUtilFlagMask);
        Assert.Equal(2, filesLayout.FileHandles.Length);

        byte[] payload = Enumerable.Range(0, checked((int)(stripeUnit * 2 + (stripeUnit / 2))))
            .Select(static i => (byte)(i % 251))
            .ToArray();

        await mdsClient.WriteStripedAsync(file, 0, payload, pnfsSession, Token);
        byte[] actual = await mdsClient.ReadStripedAsync(file, 0, payload.Length, pnfsSession, Token);
        Assert.Equal(payload, actual);
        Assert.All(dsPrograms, static program => Assert.True(program.WriteCount > 0));
        Assert.All(dsPrograms, static program => Assert.True(program.ReadCount > 0));
        Assert.Equal((ulong)payload.Length, (await fileSystem.GetAttributesAsync(backing, Token)).Size);
    }

    [Fact]
    public async Task PnfsFilesLayout_SingleDataServerDevice_FallsBackToMetadataServerIo()
    {
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "single-ds-fallback.bin", []);
        await using var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new Nfs4Program(fileSystem));
        server.Start();

        await using Nfs4Client nfs = await ConnectAsync(server);
        (_, byte[] sessionId) = await EstablishSessionAsync(nfs, "pnfs-single-ds", 0);
        var pnfsSession = new Nfs4PnfsSession(sessionId);
        var file = new Nfs4Handle { Data = backing.ToArray() };
        byte[] payload = Enumerable.Range(0, 9000).Select(static i => (byte)(255 - (i % 251))).ToArray();

        await nfs.WriteStripedAsync(file, 0, payload, pnfsSession, Token);
        byte[] actual = await nfs.ReadStripedAsync(file, 0, payload.Length, pnfsSession, Token);

        Assert.Equal(payload, actual);
        NfsReadResult stored = await fileSystem.ReadAsync(backing, 0, (uint)payload.Length, Token);
        Assert.Equal(payload, stored.Data.ToArray());
    }

    [Fact]
    public async Task BackChannelRecall_UsesCbSequenceThenDelegReturnAllowsRetry()
    {
        const uint callbackProgram = 0x40000123;
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "recall41.txt", [1]);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start(callbackProgram);
        var transport = new TcpBackChannelTransport(callback.EndPoint);
        var program = new Nfs4Program(fileSystem, backChannelTransport: transport);
        await using var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), program);
        server.Start();
        Nfs4Client nfs = await ConnectAsync(server);

        (ulong clientA, byte[] sessionA) = await EstablishSessionAsync(nfs, "v41-client-a", callbackProgram);
        (ulong clientB, byte[] sessionB) = await EstablishSessionAsync(nfs, "v41-client-b", 0);
        await CompleteReclaimAsync(nfs, sessionA, 1);

        Nfs4CompoundResult openA = await nfs.CompoundAsync(
            "open-a",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionA, 2, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientA,
                    Owner = "owner-a"u8.ToArray(),
                    Name = "recall41.txt",
                },
            ],
            Token);
        var openResultA = Assert.IsType<Nfs4OpenResult>(openA.Operations[2]);
        Assert.Equal(Nfs4OpenResult.DelegationRead, openResultA.DelegationType);

        Nfs4CompoundResult blocked = await nfs.CompoundAsync(
            "open-b",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 1, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Write,
                    ClientId = clientB,
                    Owner = "owner-b"u8.ToArray(),
                    Name = "recall41.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Delay, blocked.Status);

        Nfs4CallbackRecallOp recall = await callback.WaitForRecallAsync(Token);
        Assert.Equal(openResultA.DelegationStateId.Other, recall.StateId.Other);
        Assert.NotNull(transport.LastCompound);
        Assert.IsType<Nfs4CallbackSequenceOp>(transport.LastCompound!.Operations[0]);
        Assert.IsType<Nfs4CallbackRecallOp>(transport.LastCompound.Operations[1]);

        Nfs4CompoundResult returned = await nfs.CompoundAsync(
            "delegreturn",
            Nfs4.MinorVersion1,
            [Sequence(sessionA, 3, 0, false), new Nfs4DelegReturnOp { StateId = openResultA.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, returned.Status);

        Nfs4CompoundResult retry = await nfs.CompoundAsync(
            "open-b-retry",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 2, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 2,
                    ShareAccess = Nfs4ShareAccess.Write,
                    ClientId = clientB,
                    Owner = "owner-b"u8.ToArray(),
                    Name = "recall41.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, retry.Status);
    }

    [Fact]
    public async Task BackChannelRecall_UsesSingleForeChannelTcpConnection()
    {
        const uint callbackProgram = 0x40000125;
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateFile(fileSystem.Root, "recall41-single.txt", [1]);
        await using var server = StartServer(fileSystem);
        await using RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        var callback = new RecordingCallbackProgram(callbackProgram);
        rpc.RegisterProgram(callback);
        var nfs = new Nfs4Client(rpc);

        (ulong clientA, byte[] sessionA) = await EstablishSessionAsync(nfs, "v41-single-a", callbackProgram);
        (ulong clientB, byte[] sessionB) = await EstablishSessionAsync(nfs, "v41-single-b", 0);
        await CompleteReclaimAsync(nfs, sessionA, 1);

        Nfs4CompoundResult openA = await nfs.CompoundAsync(
            "open-a-single",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionA, 2, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Read,
                    ClientId = clientA,
                    Owner = "owner-a"u8.ToArray(),
                    Name = "recall41-single.txt",
                },
            ],
            Token);
        var openResultA = Assert.IsType<Nfs4OpenResult>(openA.Operations[2]);
        Assert.Equal(Nfs4OpenResult.DelegationRead, openResultA.DelegationType);

        Nfs4CompoundResult blocked = await nfs.CompoundAsync(
            "open-b-single",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 1, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Write,
                    ClientId = clientB,
                    Owner = "owner-b"u8.ToArray(),
                    Name = "recall41-single.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Delay, blocked.Status);

        Nfs4CallbackRecallOp recall = await callback.WaitForRecallAsync(Token);
        Assert.Equal(openResultA.DelegationStateId.Other, recall.StateId.Other);
        Assert.NotNull(callback.LastCompound);
        Assert.IsType<Nfs4CallbackSequenceOp>(callback.LastCompound!.Operations[0]);
        Assert.IsType<Nfs4CallbackRecallOp>(callback.LastCompound.Operations[1]);

        Nfs4CompoundResult returned = await nfs.CompoundAsync(
            "delegreturn-single",
            Nfs4.MinorVersion1,
            [Sequence(sessionA, 3, 0, false), new Nfs4DelegReturnOp { StateId = openResultA.DelegationStateId }],
            Token);
        Assert.Equal(Nfs4Status.Ok, returned.Status);

        Nfs4CompoundResult retry = await nfs.CompoundAsync(
            "open-b-retry-single",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 2, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 2,
                    ShareAccess = Nfs4ShareAccess.Write,
                    ClientId = clientB,
                    Owner = "owner-b"u8.ToArray(),
                    Name = "recall41-single.txt",
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, retry.Status);
    }

    [Fact]
    public async Task BlockingLockDenied_UnlockSendsNotifyLock_ThenRetrySucceeds()
    {
        const uint callbackProgram = 0x40000125;
        var fileSystem = new InMemoryFileSystem();
        NfsFileHandle backing = fileSystem.CreateFile(fileSystem.Root, "notify-lock.bin", new byte[32]);
        await using Nfs4CallbackHost callback = Nfs4CallbackHost.Start(callbackProgram);
        var transport = new TcpBackChannelTransport(callback.EndPoint);
        await using var server = StartServer(fileSystem, transport);
        Nfs4Client nfs = await ConnectAsync(server);
        var file = new Nfs4Handle { Data = backing.ToArray() };

        (ulong clientA, byte[] sessionA) = await EstablishSessionAsync(nfs, "lock-client-a", 0);
        (ulong clientB, byte[] sessionB) = await EstablishSessionAsync(nfs, "lock-client-b", callbackProgram);
        await CompleteReclaimAsync(nfs, sessionA, 1);

        Nfs4StateId openA = await OpenAsync(nfs, sessionA, 2, clientA, "open-a", "notify-lock.bin");
        Nfs4StateId openB = await OpenAsync(nfs, sessionB, 1, clientB, "open-b", "notify-lock.bin");

        Nfs4CompoundResult lockA = await nfs.CompoundAsync(
            "lock-a",
            Nfs4.MinorVersion1,
            [Sequence(sessionA, 3, 0, false), new Nfs4PutFhOp { Handle = file }, NewLock(clientA, "lock-a", openA, 0, 8)],
            Token);
        Assert.Equal(Nfs4Status.Ok, lockA.Status);
        Nfs4StateId lockStateA = Assert.IsType<Nfs4LockResult>(lockA.Operations[^1]).StateId;

        Nfs4CompoundResult denied = await nfs.CompoundAsync(
            "lock-b-denied",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 2, 0, false),
                new Nfs4PutFhOp { Handle = file },
                NewLock(clientB, "lock-b", openB, 4, 8, Nfs4LockType.WriteBlocking),
            ],
            Token);
        Assert.Equal(Nfs4Status.LockDenied, denied.Status);

        Nfs4CompoundResult unlock = await nfs.CompoundAsync(
            "unlock-a",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionA, 4, 0, false),
                new Nfs4PutFhOp { Handle = file },
                new Nfs4LockUnlockOp
                {
                    LockType = Nfs4LockType.Write,
                    Seqid = 2,
                    LockStateId = lockStateA,
                    Offset = 0,
                    Length = 8,
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, unlock.Status);

        Nfs4CallbackNotifyLockOp notifyLock = await callback.WaitForLockNotificationAsync(Token);
        Assert.Equal(file.Data, notifyLock.Handle.Data);
        Assert.Equal(clientB, notifyLock.Owner.ClientId);
        Assert.NotNull(transport.LastCompound);
        Assert.IsType<Nfs4CallbackSequenceOp>(transport.LastCompound!.Operations[0]);
        Assert.IsType<Nfs4CallbackNotifyLockOp>(transport.LastCompound.Operations[1]);

        Nfs4CompoundResult retry = await nfs.CompoundAsync(
            "lock-b-retry",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionB, 3, 0, false),
                new Nfs4PutFhOp { Handle = file },
                NewLock(clientB, "lock-b", openB, 4, 8),
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, retry.Status);
    }

    private static Nfs4SequenceOp Sequence(byte[] sessionId, uint sequenceId, uint slot, bool cacheThis) => new()
    {
        SessionId = sessionId,
        SequenceId = sequenceId,
        SlotId = slot,
        HighestSlotId = 0,
        CacheThis = cacheThis,
    };

    private static Nfs4LockOp NewLock(
        ulong clientId,
        string owner,
        Nfs4StateId openStateId,
        ulong offset,
        ulong length,
        Nfs4LockType lockType = Nfs4LockType.Write) => new()
        {
            LockType = lockType,
            NewLockOwner = true,
            OpenSeqid = 1,
            OpenStateId = openStateId,
            LockSeqid = 1,
            LockOwner = new Nfs4LockOwner(clientId, Encoding.UTF8.GetBytes(owner)),
            Offset = offset,
            Length = length,
        };

    private static async ValueTask<Nfs4StateId> OpenAsync(
        Nfs4Client nfs,
        byte[] sessionId,
        uint sequenceId,
        ulong clientId,
        string owner,
        string name)
    {
        Nfs4CompoundResult open = await nfs.CompoundAsync(
            "open",
            Nfs4.MinorVersion1,
            [
                Sequence(sessionId, sequenceId, 0, false),
                new Nfs4PutRootFhOp(),
                new Nfs4OpenOp
                {
                    Seqid = 1,
                    ShareAccess = Nfs4ShareAccess.Both,
                    ClientId = clientId,
                    Owner = Encoding.UTF8.GetBytes(owner),
                    OpenType = Nfs4OpenType.NoCreate,
                    Name = name,
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, open.Status);
        return Assert.IsType<Nfs4OpenResult>(open.Operations[^1]).StateId;
    }

    private static ValueTask<(ulong ClientId, byte[] SessionId)> EstablishSessionAsync(Nfs4Client nfs) =>
        EstablishSessionAsync(nfs, "v41-client", 0);

    private static async ValueTask<(ulong ClientId, byte[] SessionId)> EstablishSessionAsync(
        Nfs4Client nfs,
        string ownerId,
        uint callbackProgram)
    {
        Nfs4CompoundResult exchange = await nfs.CompoundAsync(
            "exchange",
            Nfs4.MinorVersion1,
            [new Nfs4ExchangeIdOp { Verifier = new byte[8], OwnerId = System.Text.Encoding.UTF8.GetBytes(ownerId) }],
            Token);
        Assert.Equal(Nfs4Status.Ok, exchange.Status);
        var exchangeResult = Assert.IsType<Nfs4ExchangeIdResult>(exchange.Operations[0]);

        Nfs4CompoundResult create = await nfs.CompoundAsync(
            "create-session",
            Nfs4.MinorVersion1,
            [
                new Nfs4CreateSessionOp
                {
                    ClientId = exchangeResult.ClientId,
                    Sequence = exchangeResult.SequenceId,
                    ForeChannel = Nfs4ChannelAttributes.Default(4),
                    BackChannel = Nfs4ChannelAttributes.Default(1),
                    Flags = callbackProgram == 0 ? 0 : Nfs4CreateSessionOp.FlagConnectionBackChannel,
                    CallbackProgram = callbackProgram,
                },
            ],
            Token);
        Assert.Equal(Nfs4Status.Ok, create.Status);
        var sessionResult = Assert.IsType<Nfs4CreateSessionResult>(create.Operations[0]);
        return (exchangeResult.ClientId, sessionResult.SessionId);
    }

    private static async ValueTask CompleteReclaimAsync(Nfs4Client nfs, byte[] sessionId, uint sequenceId)
    {
        Nfs4CompoundResult result = await nfs.CompoundAsync(
            "reclaim-complete",
            Nfs4.MinorVersion1,
            [Sequence(sessionId, sequenceId, 0, false), new Nfs4ReclaimCompleteOp()],
            Token);
        Assert.Equal(Nfs4Status.Ok, result.Status);
    }

    private static string UniversalAddress(IPEndPoint endPoint)
    {
        int port = endPoint.Port;
        return $"127.0.0.1.{port >> 8}.{port & 0xFF}";
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcServer StartServer(INfsFileSystem fileSystem)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new NfsProgram(fileSystem));
        server.Start();
        return server;
    }

    private static RpcServer StartServer(INfsFileSystem fileSystem, INfs41BackChannelTransport transport)
    {
        var server = new RpcServer(
            new IPEndPoint(IPAddress.Loopback, 0),
            new Nfs4Program(fileSystem, backChannelTransport: transport));
        server.Start();
        return server;
    }

    private static async ValueTask<Nfs4Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, Token);
        return new Nfs4Client(rpc);
    }

    private sealed class CountingNfs4Program : IRpcProgram, IRpcSecurityAware, IRpcLocalEndPointAware
    {
        private readonly Nfs4Program _inner;

        public CountingNfs4Program(INfsFileSystem fileSystem)
        {
            _inner = new Nfs4Program(fileSystem);
        }

        public uint Program => _inner.Program;

        public int ReadCount { get; private set; }

        public int WriteCount { get; private set; }

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            CountIo(request, arguments);
            return _inner.InvokeAsync(request, arguments, cancellationToken);
        }

        public void SetRpcSecGssEnabled(bool enabled) => _inner.SetRpcSecGssEnabled(enabled);

        public void SetRpcLocalEndPoint(IPEndPoint endPoint) => _inner.SetRpcLocalEndPoint(endPoint);

        private void CountIo(RpcCallInfo request, ReadOnlyMemory<byte> arguments)
        {
            if (request.Version != Nfs4.ProtocolVersion || request.Procedure != (uint)Nfs4Procedure.Compound)
            {
                return;
            }

            Nfs4CompoundArgs compound = DecodeCompound(arguments);
            foreach (Nfs4ArgOp operation in compound.Operations)
            {
                if (operation is Nfs4ReadOp)
                {
                    ReadCount++;
                }
                else if (operation is Nfs4WriteOp)
                {
                    WriteCount++;
                }
            }
        }

        private static Nfs4CompoundArgs DecodeCompound(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            return Nfs4CompoundArgs.Decode(ref reader);
        }
    }

    private sealed class TcpBackChannelTransport(IPEndPoint endPoint) : INfs41BackChannelTransport
    {
        public Nfs4CallbackCompoundArgs? LastCompound { get; private set; }

        public async ValueTask<Nfs4CallbackCompoundResult> CallAsync(
            Nfs41BackChannelCall backChannelCall,
            CancellationToken cancellationToken = default)
        {
            LastCompound = backChannelCall.Compound;
            await using RpcClient rpc = await RpcClient.ConnectAsync(endPoint, cancellationToken);
            RpcReply reply = await rpc.CallAsync(
                backChannelCall.CallbackProgram,
                Nfs4.ProtocolVersion,
                (uint)Nfs4CallbackProcedure.Compound,
                OpaqueAuth.None,
                OpaqueAuth.None,
                backChannelCall.Compound,
                cancellationToken);
            Assert.True(reply.IsSuccess);
            return reply.DecodeResult<Nfs4CallbackCompoundResult>();
        }
    }

    private sealed class RecordingCallbackProgram(uint program) : IRpcProgram
    {
        private readonly object _gate = new();
        private readonly Queue<Nfs4CallbackRecallOp> _recalls = new();
        private readonly Queue<TaskCompletionSource<Nfs4CallbackRecallOp>> _waiters = new();

        public uint Program { get; } = program;

        public Nfs4CallbackCompoundArgs? LastCompound { get; private set; }

        public ValueTask<Nfs4CallbackRecallOp> WaitForRecallAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_recalls.TryDequeue(out Nfs4CallbackRecallOp? recall))
                {
                    return ValueTask.FromResult(recall);
                }

                var waiter = new TaskCompletionSource<Nfs4CallbackRecallOp>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                if (cancellationToken.CanBeCanceled)
                {
                    cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
                }

                _waiters.Enqueue(waiter);
                return new ValueTask<Nfs4CallbackRecallOp>(waiter.Task);
            }
        }

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != Nfs4.ProtocolVersion)
            {
                return new ValueTask<RpcReplyPayload>(
                    RpcReplyPayload.ProgramMismatch(Nfs4.ProtocolVersion, Nfs4.ProtocolVersion));
            }

            if ((Nfs4CallbackProcedure)request.Procedure != Nfs4CallbackProcedure.Compound)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProcedureUnavailable());
            }

            Nfs4CallbackCompoundArgs args = Decode(arguments);
            LastCompound = args;
            var result = new Nfs4CallbackCompoundResult { Status = Nfs4Status.Ok, Tag = args.Tag };
            foreach (Nfs4CallbackArgOp operation in args.Operations)
            {
                Nfs4CallbackResOp resop = Execute(operation);
                result.Operations.Add(resop);
                result.OperationStatuses.Add(resop.Status);
                if (resop.Status != Nfs4Status.Ok)
                {
                    result.Status = resop.Status;
                    break;
                }
            }

            return new ValueTask<RpcReplyPayload>(Encode(result));
        }

        private Nfs4CallbackResOp Execute(Nfs4CallbackArgOp operation)
        {
            return operation switch
            {
                Nfs4CallbackSequenceOp sequence => new Nfs4CallbackSequenceResult
                {
                    Status = Nfs4Status.Ok,
                    SessionId = sequence.SessionId,
                    SequenceId = sequence.SequenceId,
                    SlotId = sequence.SlotId,
                    HighestSlotId = sequence.HighestSlotId,
                    TargetHighestSlotId = sequence.HighestSlotId,
                },
                Nfs4CallbackRecallOp recall => Recall(recall),
                _ => new Nfs4CallbackStatusResult(operation.Op) { Status = Nfs4Status.NotSupported },
            };
        }

        private Nfs4CallbackStatusResult Recall(Nfs4CallbackRecallOp recall)
        {
            lock (_gate)
            {
                if (_waiters.TryDequeue(out TaskCompletionSource<Nfs4CallbackRecallOp>? waiter))
                {
                    waiter.TrySetResult(recall);
                }
                else
                {
                    _recalls.Enqueue(recall);
                }
            }

            return new Nfs4CallbackStatusResult(Nfs4CallbackOp.Recall) { Status = Nfs4Status.Ok };
        }

        private static Nfs4CallbackCompoundArgs Decode(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            return Nfs4CallbackCompoundArgs.ReadFrom(ref reader);
        }

        private static RpcReplyPayload Encode(Nfs4CallbackCompoundResult result)
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            result.WriteTo(ref writer);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
