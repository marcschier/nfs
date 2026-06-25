using System.Net;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcLoopbackTests
{
    [Fact]
    public async Task NullProcedure_Succeeds()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync<XdrVoid>(
            EchoProgram.Number, version: 1, procedure: 0, OpaqueAuth.None, OpaqueAuth.None, default,
            TestContext.Current.CancellationToken);

        Assert.True(reply.IsSuccess);
    }

    [Fact]
    public async Task EchoProcedure_ReturnsIncrementedValue()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync(
            EchoProgram.Number, version: 1, procedure: 1, OpaqueAuth.None, OpaqueAuth.None, new U32(41),
            TestContext.Current.CancellationToken);

        Assert.True(reply.IsSuccess);
        Assert.Equal(42u, reply.DecodeResult<U32>().Value);
    }

    [Fact]
    public async Task EchoProcedure_WithAuthSys_Succeeds()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync(
            EchoProgram.Number, version: 1, procedure: 1,
            AuthSys.Create(1000, 1000, "test-host"), OpaqueAuth.None, new U32(7),
            TestContext.Current.CancellationToken);

        Assert.Equal(8u, reply.DecodeResult<U32>().Value);
    }

    [Fact]
    public async Task UnknownProcedure_ReturnsProcedureUnavailable()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync<XdrVoid>(
            EchoProgram.Number, version: 1, procedure: 99, OpaqueAuth.None, OpaqueAuth.None, default,
            TestContext.Current.CancellationToken);

        Assert.False(reply.IsSuccess);
        Assert.Equal(AcceptStatus.ProcedureUnavailable, reply.Header.Accept);
    }

    [Fact]
    public async Task UnknownProgram_ReturnsProgramUnavailable()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync<XdrVoid>(
            program: 0xDEADBEEF, version: 1, procedure: 0, OpaqueAuth.None, OpaqueAuth.None, default,
            TestContext.Current.CancellationToken);

        Assert.False(reply.IsSuccess);
        Assert.Equal(AcceptStatus.ProgramUnavailable, reply.Header.Accept);
    }

    [Fact]
    public async Task VersionMismatch_ReturnsProgramMismatch()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallAsync<XdrVoid>(
            EchoProgram.Number, version: 2, procedure: 0, OpaqueAuth.None, OpaqueAuth.None, default,
            TestContext.Current.CancellationToken);

        Assert.Equal(AcceptStatus.ProgramMismatch, reply.Header.Accept);
        Assert.Equal(1u, reply.Header.MismatchLow);
        Assert.Equal(1u, reply.Header.MismatchHigh);
    }

    [Fact]
    public async Task MultipleSequentialCalls_ReuseTheConnection()
    {
        await using var server = StartServer();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        for (uint i = 0; i < 10; i++)
        {
            RpcReply reply = await client.CallAsync(
                EchoProgram.Number, version: 1, procedure: 1, OpaqueAuth.None, OpaqueAuth.None, new U32(i),
                TestContext.Current.CancellationToken);
            Assert.Equal(i + 1, reply.DecodeResult<U32>().Value);
        }
    }

    [Fact]
    public async Task InboundCallWhileForeCallIsPending_DemultiplexesByXid()
    {
        const uint callbackProgram = 0x20000002;
        await using var server = StartServer(new BackCallingProgram(callbackProgram));
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);
        client.RegisterProgram(new EchoProgram(callbackProgram));

        RpcReply reply = await client.CallAsync(
            BackCallingProgram.Number,
            version: 1,
            procedure: 1,
            OpaqueAuth.None,
            OpaqueAuth.None,
            new U32(41),
            TestContext.Current.CancellationToken);

        Assert.True(reply.IsSuccess);
        Assert.Equal(43u, reply.DecodeResult<U32>().Value);
    }

    private static RpcServer StartServer() => StartServer(new EchoProgram(EchoProgram.Number));

    private static RpcServer StartServer(IRpcProgram program)
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), program);
        server.Start();
        return server;
    }

    private readonly record struct U32(uint Value) : IXdrSerializable<U32>
    {
        public void WriteTo(ref XdrWriter writer) => writer.WriteUInt32(Value);

        public static U32 ReadFrom(ref XdrReader reader) => new(reader.ReadUInt32());
    }

    private sealed class EchoProgram : IRpcProgram
    {
        public const uint Number = 0x20000001;

        public EchoProgram(uint program)
        {
            Program = program;
        }

        public uint Program { get; }

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != 1)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProgramMismatch(1, 1));
            }

            RpcReplyPayload payload = request.Procedure switch
            {
                0 => RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty),
                1 => Echo(arguments),
                _ => RpcReplyPayload.ProcedureUnavailable(),
            };
            return new ValueTask<RpcReplyPayload>(payload);
        }

        private static RpcReplyPayload Echo(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            uint value = reader.ReadUInt32();

            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteUInt32(value + 1);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }

    private sealed class BackCallingProgram(uint callbackProgram) : IRpcProgram
    {
        public const uint Number = 0x20000003;

        public uint Program => Number;

        public async ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Connection is null)
            {
                return RpcReplyPayload.SystemError();
            }

            RpcReply callback = await request.Connection.CallAsync(
                callbackProgram,
                version: 1,
                procedure: 1,
                OpaqueAuth.None,
                OpaqueAuth.None,
                Decode(arguments),
                cancellationToken);
            if (!callback.IsSuccess)
            {
                return RpcReplyPayload.SystemError();
            }

            U32 value = callback.DecodeResult<U32>();
            return Encode(new U32(value.Value + 1));
        }

        private static U32 Decode(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            return U32.ReadFrom(ref reader);
        }

        private static RpcReplyPayload Encode(U32 value)
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            value.WriteTo(ref writer);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
