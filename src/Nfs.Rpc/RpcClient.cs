using System.Net;
using System.Net.Sockets;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A minimal ONC/RPC client over TCP. Calls on a single instance are serialized: each call sends a
/// request and awaits its reply before the next begins.
/// </summary>
public sealed class RpcClient : IRpcClient
{
    private readonly object _programGate = new();
    private readonly Dictionary<uint, IRpcProgram> _programs = new();
    private readonly RpcDuplexConnection _connection;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    private RpcClient(NetworkStream stream)
    {
        _connection = new RpcDuplexConnection(stream, DispatchInboundCallAsync);
    }

    /// <summary>Connects to an RPC server over TCP.</summary>
    /// <param name="endPoint">The server endpoint.</param>
    /// <param name="cancellationToken">A token to cancel the connect.</param>
    /// <returns>A connected client.</returns>
    public static async ValueTask<RpcClient> ConnectAsync(
        EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        return new RpcClient(new NetworkStream(socket, ownsSocket: true));
    }

    /// <summary>Registers a program for inbound CALL records on this client connection.</summary>
    /// <param name="program">The callback program to dispatch.</param>
    public void RegisterProgram(IRpcProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_programGate)
        {
            _programs[program.Program] = program;
        }
    }

    /// <summary>Invokes a remote procedure and awaits its reply.</summary>
    /// <typeparam name="TArgs">The procedure argument type.</typeparam>
    /// <param name="program">The remote program number.</param>
    /// <param name="version">The remote program version.</param>
    /// <param name="procedure">The procedure number.</param>
    /// <param name="credential">The caller's credential.</param>
    /// <param name="verifier">The caller's verifier.</param>
    /// <param name="arguments">The procedure arguments.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The decoded reply.</returns>
    public async ValueTask<RpcReply> CallAsync<TArgs>(
        uint program,
        uint version,
        uint procedure,
        OpaqueAuth credential,
        OpaqueAuth verifier,
        TArgs arguments,
        CancellationToken cancellationToken = default)
        where TArgs : IXdrSerializable<TArgs>
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _connection.CallAsync(
                program,
                version,
                procedure,
                credential,
                verifier,
                arguments,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Invokes a remote procedure using an established RPCSEC_GSS context.</summary>
    /// <typeparam name="TArgs">The procedure argument type.</typeparam>
    /// <param name="program">The remote program number.</param>
    /// <param name="version">The remote program version.</param>
    /// <param name="procedure">The procedure number.</param>
    /// <param name="rpcSecGss">The established RPCSEC_GSS client context.</param>
    /// <param name="service">The RPCSEC_GSS protection service to use for arguments and results.</param>
    /// <param name="arguments">The procedure arguments.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The decoded reply with protected results unwrapped on success.</returns>
    public async ValueTask<RpcReply> CallRpcSecGssAsync<TArgs>(
        uint program,
        uint version,
        uint procedure,
        RpcSecGssClientContext rpcSecGss,
        RpcSecGssService service,
        TArgs arguments,
        CancellationToken cancellationToken = default)
        where TArgs : IXdrSerializable<TArgs>
    {
        ArgumentNullException.ThrowIfNull(rpcSecGss);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _connection.CallRpcSecGssAsync(
                program,
                version,
                procedure,
                rpcSecGss,
                service,
                arguments,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _connection.DisposeAsync().ConfigureAwait(false);
        _gate.Dispose();
    }

    private async ValueTask<byte[]?> DispatchInboundCallAsync(
        RpcDuplexConnection connection,
        byte[] message,
        CancellationToken cancellationToken)
    {
        RpcCallHeader header;
        int argumentsOffset;
        try
        {
            (header, argumentsOffset) = RpcMessageCodec.ParseCallHeader(message);
        }
        catch (Exception ex) when (ex is XdrException or RpcException)
        {
            return null;
        }

        IRpcProgram? program;
        lock (_programGate)
        {
            _programs.TryGetValue(header.Program, out program);
        }

        if (program is null)
        {
            return RpcMessageCodec.EncodeReply(header.Xid, RpcReplyPayload.ProgramUnavailable());
        }

        ReadOnlyMemory<byte> arguments = new(message, argumentsOffset, message.Length - argumentsOffset);
        var call = new RpcCallInfo(
            header.Xid,
            header.Version,
            header.Procedure,
            header.Credential,
            header.Verifier,
            connection);

        RpcReplyPayload payload;
        try
        {
            payload = await program.InvokeAsync(call, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            payload = RpcReplyPayload.SystemError();
        }

        return RpcMessageCodec.EncodeReply(header.Xid, payload);
    }
}
