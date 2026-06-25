using System.Buffers;
using System.Net;

using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class PortmapTests
{
    [Fact]
    public async Task GetPort_ReturnsRegisteredPort()
    {
        await using var server = StartPortmap();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        int port = await PortmapClient.GetPortAsync(
            client, program: 100003, version: 3, PortmapProtocol.Tcp, TestContext.Current.CancellationToken);

        Assert.Equal(2049, port);
    }

    [Fact]
    public async Task GetPort_UnregisteredProgram_ReturnsZero()
    {
        await using var server = StartPortmap();
        await using RpcClient client = await RpcClient.ConnectAsync(server.LocalEndPoint, TestContext.Current.CancellationToken);

        int port = await PortmapClient.GetPortAsync(
            client, program: 999999, version: 1, PortmapProtocol.Udp, TestContext.Current.CancellationToken);

        Assert.Equal(0, port);
    }

    private static RpcServer StartPortmap()
    {
        var server = new RpcServer(new IPEndPoint(IPAddress.Loopback, 0), new FakePortmapProgram());
        server.Start();
        return server;
    }

    private sealed class FakePortmapProgram : IRpcProgram
    {
        public uint Program => Portmap.Program;

        public ValueTask<RpcReplyPayload> InvokeAsync(
            RpcCallInfo request,
            ReadOnlyMemory<byte> arguments,
            CancellationToken cancellationToken)
        {
            if (request.Version != Portmap.Version)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProgramMismatch(2, 2));
            }

            if (request.Procedure != Portmap.GetPortProcedure)
            {
                return new ValueTask<RpcReplyPayload>(RpcReplyPayload.ProcedureUnavailable());
            }

            return new ValueTask<RpcReplyPayload>(GetPort(arguments));
        }

        private static RpcReplyPayload GetPort(ReadOnlyMemory<byte> arguments)
        {
            var reader = new XdrReader(arguments.Span);
            uint program = reader.ReadUInt32();
            _ = reader.ReadUInt32(); // version
            _ = reader.ReadUInt32(); // protocol
            _ = reader.ReadUInt32(); // requested port (0 for GETPORT)

            uint port = program == 100003 ? 2049u : 0u;

            var buffer = new ArrayBufferWriter<byte>();
            var writer = new XdrWriter(buffer);
            writer.WriteUInt32(port);
            return RpcReplyPayload.Success(buffer.WrittenSpan.ToArray());
        }
    }
}
