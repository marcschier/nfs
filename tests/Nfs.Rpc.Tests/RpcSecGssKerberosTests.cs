using System.Buffers;
using System.Net;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcSecGssKerberosTests
{
    [Theory]
    [InlineData(RpcSecGssService.Integrity)]
    [InlineData(RpcSecGssService.Privacy)]
    public async Task ProtectedDataCall_WithKerberos_RoundTrips(RpcSecGssService service)
    {
        SkipUnlessKerberosTestEnabled();

        var program = new SecuredEchoProgram();
        await using var server = StartServer(program);
        await using RpcClient client = await RpcClient.ConnectAsync(
            server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcSecGssClientContext context = await RpcSecGssClientContext.EstablishAsync(
            client,
            SecuredEchoProgram.Number,
            version: 1,
            new KerberosGssMechanism(),
            Environment.GetEnvironmentVariable("NFS_KRB5_SERVICE_NAME") ?? "nfs@localhost",
            TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallRpcSecGssAsync(
            SecuredEchoProgram.Number,
            version: 1,
            procedure: 1,
            context,
            service,
            new U32(41),
            TestContext.Current.CancellationToken);

        Assert.True(reply.IsSuccess);
        Assert.Equal(42u, reply.DecodeResult<U32>().Value);
        Assert.Equal(service, program.LastService);
        Assert.False(program.LastHandle.IsEmpty);
    }

    private static void SkipUnlessKerberosTestEnabled()
    {
        if (Environment.GetEnvironmentVariable("NFS_KRB5_TEST") != "1")
        {
            Assert.Skip("Set NFS_KRB5_TEST=1 with a configured KDC, ticket cache, and keytab to run Kerberos GSS tests.");
        }

        if (!KerberosGssMechanism.IsSupported)
        {
            Assert.Skip("Kerberos GSS is not supported on this operating system.");
        }
    }

    private static RpcServer StartServer(SecuredEchoProgram program)
    {
        var security = new RpcSecGssServer(new KerberosGssMechanism());
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), program, security);
        server.Start();
        return server;
    }

    private readonly record struct U32(uint Value) : IXdrSerializable<U32>
    {
        public void WriteTo(ref XdrWriter writer) => writer.WriteUInt32(Value);

        public static U32 ReadFrom(ref XdrReader reader) => new(reader.ReadUInt32());
    }

    private sealed class SecuredEchoProgram : IRpcProgram
    {
        public const uint Number = 0x20000029;

        public uint Program => Number;

        public RpcSecGssService? LastService { get; private set; }

        public ReadOnlyMemory<byte> LastHandle { get; private set; }

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != 1)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProgramMismatch(1, 1));
            }

            if (request.Procedure == 0)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty));
            }

            if (request.RpcSecGss is null)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.SystemError());
            }

            LastService = request.RpcSecGss.Service;
            LastHandle = request.RpcSecGss.Handle;
            return new ValueTask<RpcReplyPayload>(Echo(arguments));
        }

        private static RpcReplyPayload Echo(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            uint value = reader.ReadUInt32();

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteUInt32(value + 1);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
