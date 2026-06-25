using System.Net;
using System.Net.Sockets;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcUdpLoopbackTests
{
    [Fact]
    public async Task NullProcedure_OverUdp_Succeeds()
    {
        await using var server = StartServer(out _);
        await using RpcUdpClient client = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);

        RpcReply reply = await client.CallAsync<XdrVoid>(
            CountingProgram.Number, version: 1, procedure: 0, OpaqueAuth.None, OpaqueAuth.None, default, Token);

        Assert.True(reply.IsSuccess);
    }

    [Fact]
    public async Task EchoProcedure_OverUdp_ReturnsIncrementedValue()
    {
        await using var server = StartServer(out _);
        await using RpcUdpClient client = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);

        RpcReply reply = await client.CallAsync(
            CountingProgram.Number, version: 1, procedure: 1, OpaqueAuth.None, OpaqueAuth.None, new U32(41), Token);

        Assert.True(reply.IsSuccess);
        Assert.Equal(42u, reply.DecodeResult<U32>().Value);
    }

    [Fact]
    public async Task MultipleSequentialCalls_OverUdp()
    {
        await using var server = StartServer(out CountingProgram program);
        await using RpcUdpClient client = await RpcUdpClient.ConnectAsync(server.LocalEndPoint, cancellationToken: Token);

        for (uint i = 0; i < 10; i++)
        {
            RpcReply reply = await client.CallAsync(
                CountingProgram.Number, version: 1, procedure: 1, OpaqueAuth.None, OpaqueAuth.None, new U32(i), Token);
            Assert.Equal(i + 1, reply.DecodeResult<U32>().Value);
        }

        Assert.Equal(10, program.Invocations);
    }

    [Fact]
    public async Task DuplicateRequest_OverUdp_ExecutesOnce()
    {
        // Two datagrams carry the same XID; the duplicate-request cache must replay the first reply
        // without invoking the (non-idempotent) procedure a second time.
        await using var server = StartServer(out CountingProgram program);

        using var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(server.LocalEndPoint, Token);

        byte[] message = EncodeCall(xid: 42, CountingProgram.Number, version: 1, procedure: 2, new U32(0));
        byte[] firstBuffer = new byte[1024];
        byte[] secondBuffer = new byte[1024];

        await socket.SendAsync(message, SocketFlags.None, Token);
        int firstLength = await socket.ReceiveAsync(firstBuffer, SocketFlags.None, Token);

        await socket.SendAsync(message, SocketFlags.None, Token);
        int secondLength = await socket.ReceiveAsync(secondBuffer, SocketFlags.None, Token);

        uint firstValue = DecodeReplyValue(firstBuffer.AsSpan(0, firstLength));
        uint secondValue = DecodeReplyValue(secondBuffer.AsSpan(0, secondLength));

        Assert.Equal(firstValue, secondValue);
        Assert.Equal(1, program.Invocations);
    }

    private static byte[] EncodeCall(uint xid, uint program, uint version, uint procedure, U32 arguments)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        new RpcCallHeader(xid, program, version, procedure, OpaqueAuth.None, OpaqueAuth.None).WriteTo(ref writer);
        arguments.WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static uint DecodeReplyValue(ReadOnlySpan<byte> reply)
    {
        var reader = new XdrReader(reply);
        _ = RpcReplyHeader.ReadFrom(ref reader);
        return reader.ReadUInt32();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static RpcUdpServer StartServer(out CountingProgram program)
    {
        program = new CountingProgram();
        var server = new RpcUdpServer(new IPEndPoint(IPAddress.Loopback, 0), program);
        server.Start();
        return server;
    }

    private readonly record struct U32(uint Value) : IXdrSerializable<U32>
    {
        public void WriteTo(ref XdrWriter writer) => writer.WriteUInt32(Value);

        public static U32 ReadFrom(ref XdrReader reader) => new(reader.ReadUInt32());
    }

    private sealed class CountingProgram : IRpcProgram
    {
        public const uint Number = 0x20000002;

        private int _invocations;

        public int Invocations => _invocations;

        public uint Program => Number;

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
                2 => Count(),
                _ => RpcReplyPayload.ProcedureUnavailable(),
            };
            return new ValueTask<RpcReplyPayload>(payload);
        }

        private RpcReplyPayload Echo(ReadOnlyMemory<byte> arguments)
        {
            Interlocked.Increment(ref _invocations);
            var reader = new XdrReader(arguments.Span);
            return Encode(reader.ReadUInt32() + 1);
        }

        private RpcReplyPayload Count() => Encode((uint)Interlocked.Increment(ref _invocations));

        private static RpcReplyPayload Encode(uint value)
        {
            var buffer = new System.Buffers.ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteUInt32(value);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
