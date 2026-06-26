using System.Buffers;
using System.Net;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcSecGssSspiTests
{
    [Fact]
    public void SspiContext_MicAndWrap_RoundTrip()
    {
        SkipUnlessWindows();

        var mechanism = new KerberosGssMechanism();
        IGssContext client = mechanism.CreateClientContext("HOST/localhost");
        IGssContext server = mechanism.CreateServerContext();
        try
        {
            Establish(client, server);

            ReadOnlySpan<byte> message = "rpcsec-gss-sspi-message"u8;
            byte[] mic = client.GetMic(message);
            Assert.True(server.VerifyMic(message, mic));

            ReadOnlySpan<byte> plaintext = "rpcsec-gss-sspi-private-data"u8;
            byte[] wrapped = client.Wrap(plaintext);
            byte[] unwrapped = server.Unwrap(wrapped);
            Assert.Equal(plaintext.ToArray(), unwrapped);
        }
        finally
        {
            (client as IDisposable)?.Dispose();
            (server as IDisposable)?.Dispose();
        }
    }

    [Theory]
    [InlineData(RpcSecGssService.Integrity)]
    [InlineData(RpcSecGssService.Privacy)]
    public async Task ProtectedDataCall_WithSspi_RoundTrips(RpcSecGssService service)
    {
        SkipUnlessWindows();

        var program = new SecuredEchoProgram();
        await using var server = StartServer(program);
        await using RpcClient client = await RpcClient.ConnectAsync(
            server.LocalEndPoint, TestContext.Current.CancellationToken);

        RpcSecGssClientContext context = await RpcSecGssClientContext.EstablishAsync(
            client,
            SecuredEchoProgram.Number,
            version: 1,
            new KerberosGssMechanism(),
            "HOST/localhost",
            TestContext.Current.CancellationToken);

        RpcReply reply = await client.CallRpcSecGssAsync(
            SecuredEchoProgram.Number,
            version: 1,
            procedure: 1,
            context,
            service,
            new U32(41),
            TestContext.Current.CancellationToken);

        Assert.True(
            reply.IsSuccess,
            $"RPCSEC_GSS SSPI call failed with {reply.Header.Status}/{reply.Header.Accept}/{reply.Header.Auth}.");
        Assert.Equal(42u, reply.DecodeResult<U32>().Value);
        Assert.Equal(service, program.LastService);
        Assert.False(program.LastHandle.IsEmpty);
    }

    private static void SkipUnlessWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows SSPI tests run only on Windows.");
        }
    }

    private static void Establish(IGssContext client, IGssContext server)
    {
        GssTokenResult clientStep = client.Init(ReadOnlySpan<byte>.Empty);
        for (int i = 0; i < 10; i++)
        {
            GssTokenResult serverStep = server.Accept(clientStep.OutputToken.Span);
            AssertStepStatus(serverStep.MajorStatus);
            if (serverStep.OutputToken.IsEmpty && client.IsEstablished && server.IsEstablished)
            {
                return;
            }

            clientStep = client.Init(serverStep.OutputToken.Span);
            AssertStepStatus(clientStep.MajorStatus);
            if (clientStep.OutputToken.IsEmpty && client.IsEstablished && server.IsEstablished)
            {
                return;
            }
        }

        throw new RpcException("SSPI context did not establish within 10 token exchanges.");
    }

    private static void AssertStepStatus(GssMajorStatus status)
    {
        Assert.True(
            status is GssMajorStatus.Complete or GssMajorStatus.ContinueNeeded,
            $"Unexpected GSS status {status}.");
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
        public const uint Number = 0x20000032;

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
