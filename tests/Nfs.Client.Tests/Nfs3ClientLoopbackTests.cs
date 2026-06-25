using System.Buffers;
using System.Net;

using Nfs.Abstractions;
using Nfs.Protocol.V3;
using Nfs.Rpc;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Client.Tests;

public sealed class Nfs3ClientLoopbackTests
{
    [Fact]
    public async Task Null_Succeeds()
    {
        await using var server = StartServer(out _);
        Nfs3Client nfs = await ConnectAsync(server);

        await nfs.NullAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetAttributes_KnownHandle_ReturnsAttributes()
    {
        var handle = new Nfs3Handle { Data = [1, 2, 3, 4] };
        var attributes = new Nfs3FileAttributes
        {
            Type = NfsFileType.Regular,
            Mode = 0x1A4,
            Size = 2048,
            FileId = 42,
        };

        await using var server = StartServer(out FakeNfs3Server program);
        program.AddObject(handle, attributes);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(handle, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(attributes, result.Attributes);
    }

    [Fact]
    public async Task GetAttributes_UnknownHandle_ReturnsStale()
    {
        await using var server = StartServer(out _);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3GetAttrResult result = await nfs.GetAttributesAsync(
            new Nfs3Handle { Data = [9, 9] }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.StaleHandle, result.Status);
    }

    [Fact]
    public async Task Lookup_KnownName_ReturnsChildHandle()
    {
        var directory = new Nfs3Handle { Data = [0x10] };
        var child = new Nfs3Handle { Data = [0x20, 0x21] };

        await using var server = StartServer(out FakeNfs3Server program);
        program.AddEntry(directory, "file.txt", child);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult result = await nfs.LookupAsync(directory, "file.txt", TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Equal(child.Data, result.Ok.Handle.Data);
    }

    [Fact]
    public async Task Lookup_UnknownName_ReturnsNoEntry()
    {
        var directory = new Nfs3Handle { Data = [0x10] };

        await using var server = StartServer(out _);
        Nfs3Client nfs = await ConnectAsync(server);

        Nfs3LookupResult result = await nfs.LookupAsync(directory, "missing", TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccess);
        Assert.Equal(NfsStatus.NoEntry, result.Status);
    }

    private static RpcServer StartServer(out FakeNfs3Server program)
    {
        program = new FakeNfs3Server();
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), program);
        server.Start();
        return server;
    }

    private static async ValueTask<Nfs3Client> ConnectAsync(RpcServer server)
    {
        RpcClient rpc = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);
        return new Nfs3Client(rpc);
    }

    private sealed class FakeNfs3Server : IRpcProgram
    {
        private readonly Dictionary<string, Nfs3FileAttributes> _attributes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Nfs3Handle> _entries = new(StringComparer.Ordinal);

        public uint Program => Nfs3.Program;

        public void AddObject(Nfs3Handle handle, Nfs3FileAttributes attributes) =>
            _attributes[Key(handle)] = attributes;

        public void AddEntry(Nfs3Handle directory, string name, Nfs3Handle child) =>
            _entries[Key(directory) + "/" + name] = child;

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != Nfs3.ProtocolVersion)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProgramMismatch(3, 3));
            }

            return new ValueTask<RpcReplyPayload>(Dispatch((Nfs3Procedure)request.Procedure, arguments));
        }

        private RpcReplyPayload Dispatch(Nfs3Procedure procedure, ReadOnlyMemory<byte> arguments) => procedure switch
        {
            Nfs3Procedure.Null => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
            Nfs3Procedure.GetAttributes => GetAttributes(arguments),
            Nfs3Procedure.Lookup => Lookup(arguments),
            _ => RpcReplyPayload.ProcedureUnavailable(),
        };

        private RpcReplyPayload GetAttributes(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            Nfs3GetAttrArgs args = Nfs3GetAttrArgs.ReadFrom(ref reader);

            Nfs3GetAttrResult result = _attributes.TryGetValue(Key(args.Handle), out Nfs3FileAttributes attributes)
                ? Nfs3GetAttrResult.Success(attributes)
                : Nfs3GetAttrResult.Failure(NfsStatus.StaleHandle);
            return Encode(result);
        }

        private RpcReplyPayload Lookup(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            Nfs3LookupArgs args = Nfs3LookupArgs.ReadFrom(ref reader);
            Nfs3DirOpArgs what = args.What;

            Nfs3LookupResult result = _entries.TryGetValue(Key(what.Directory) + "/" + what.Name, out Nfs3Handle child)
                ? Nfs3LookupResult.Success(new Nfs3LookupResultOk { Handle = child })
                : Nfs3LookupResult.Failure(NfsStatus.NoEntry);
            return Encode(result);
        }

        private static RpcReplyPayload Encode<T>(T result)
            where T : IXdrSerializable<T>
        {
            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            result.WriteTo(ref writer);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }

        private static string Key(Nfs3Handle handle) => Convert.ToHexString(handle.Data);
    }
}
