using System.Net;
using System.Net.Sockets;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A minimal ONC/RPC server over TCP. It accepts connections, reads record-marked call messages,
/// dispatches them to a single <see cref="IRpcProgram"/>, and writes the replies.
/// </summary>
public sealed class RpcServer : IAsyncDisposable
{
    private readonly Socket _listener;
    private readonly Dictionary<uint, IRpcProgram> _programs;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly RpcSecGssServer? _rpcSecGss;
    private Task? _acceptLoop;
    private bool _disposed;

    /// <summary>Creates a server bound to <paramref name="endPoint"/> serving <paramref name="program"/>.</summary>
    /// <param name="endPoint">The local endpoint to bind and listen on.</param>
    /// <param name="program">The program to dispatch calls to.</param>
    /// <param name="backlog">The listen backlog.</param>
    public RpcServer(IPEndPoint endPoint, IRpcProgram program, int backlog = 128)
        : this(endPoint, new[] { program }, backlog)
    {
    }

    /// <summary>Creates a server with RPCSEC_GSS support bound to <paramref name="endPoint"/>.</summary>
    /// <param name="endPoint">The local endpoint to bind and listen on.</param>
    /// <param name="program">The program to dispatch calls to.</param>
    /// <param name="rpcSecGss">The RPCSEC_GSS context store and acceptor.</param>
    /// <param name="backlog">The listen backlog.</param>
    public RpcServer(
        IPEndPoint endPoint,
        IRpcProgram program,
        RpcSecGssServer rpcSecGss,
        int backlog = 128)
        : this(endPoint, new[] { program }, rpcSecGss, backlog)
    {
    }

    /// <summary>Creates a server bound to <paramref name="endPoint"/> serving several programs.</summary>
    /// <param name="endPoint">The local endpoint to bind and listen on.</param>
    /// <param name="programs">The programs to dispatch calls to, keyed by program number.</param>
    /// <param name="backlog">The listen backlog.</param>
    public RpcServer(IPEndPoint endPoint, IEnumerable<IRpcProgram> programs, int backlog = 128)
        : this(endPoint, programs, rpcSecGss: null, backlog)
    {
    }

    /// <summary>Creates a server with RPCSEC_GSS support serving several programs.</summary>
    /// <param name="endPoint">The local endpoint to bind and listen on.</param>
    /// <param name="programs">The programs to dispatch calls to, keyed by program number.</param>
    /// <param name="rpcSecGss">The RPCSEC_GSS context store and acceptor.</param>
    /// <param name="backlog">The listen backlog.</param>
    public RpcServer(
        IPEndPoint endPoint,
        IEnumerable<IRpcProgram> programs,
        RpcSecGssServer? rpcSecGss,
        int backlog = 128)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(programs);

        _programs = new Dictionary<uint, IRpcProgram>();
        foreach (IRpcProgram program in programs)
        {
            ArgumentNullException.ThrowIfNull(program);
            if (rpcSecGss is not null && program is IRpcSecurityAware securityAware)
            {
                securityAware.SetRpcSecGssEnabled(true);
            }

            _programs[program.Program] = program;
        }

        _listener = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(endPoint);
        _listener.Listen(backlog);
        foreach (IRpcProgram program in _programs.Values)
        {
            if (program is IRpcLocalEndPointAware localEndPointAware)
            {
                localEndPointAware.SetRpcLocalEndPoint(LocalEndPoint);
            }
        }

        _rpcSecGss = rpcSecGss;
    }

    /// <summary>Gets the endpoint the server is listening on (useful when binding to port 0).</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint)_listener.LocalEndPoint!;

    /// <summary>Starts accepting connections in the background.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _acceptLoop ??= Task.Run(() => AcceptLoopAsync(_shutdown.Token));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener.Dispose();

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Socket connection;
            try
            {
                connection = await _listener.AcceptAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                // On Linux, disposing the listener while AcceptAsync is pending surfaces as a
                // SocketException (EINVAL) rather than ObjectDisposedException. During shutdown
                // this is expected; just stop accepting.
                break;
            }

            _ = HandleConnectionAsync(connection, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(Socket connection, CancellationToken cancellationToken)
    {
        var stream = new NetworkStream(connection, ownsSocket: true);
        await using var rpc = new RpcDuplexConnection(stream, ProcessAsync);
        try
        {
            await rpc.Completion.ConfigureAwait(false);
        }
#pragma warning disable CA1031, RCS1075 // Per-connection isolation: a fault on one connection must not stop the server.
        catch (Exception)
        {
            // The connection is torn down in the finally block below.
        }
#pragma warning restore CA1031, RCS1075
    }

    private async ValueTask<byte[]?> ProcessAsync(
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

        if (!_programs.TryGetValue(header.Program, out IRpcProgram? program))
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
        RpcSecGssCallContext? rpcSecGssContext = null;
        if (header.Credential.Flavor == AuthFlavor.RpcSecGss)
        {
            if (_rpcSecGss is null)
            {
                return RpcMessageCodec.EncodeAuthError(header.Xid, AuthStatus.TooWeak);
            }

            RpcSecGssCredential credential;
            try
            {
                credential = RpcSecGssWire.DecodeCredential(header.Credential);
                if (credential.Procedure is RpcSecGssProcedure.Init or RpcSecGssProcedure.ContinueInit)
                {
                    RpcSecGssServerResult initResult = _rpcSecGss.ProcessInit(arguments);
                    return RpcMessageCodec.EncodeReply(
                        header.Xid, RpcReplyPayload.Success(initResult.Payload), OpaqueAuth.None);
                }

                if (credential.Procedure == RpcSecGssProcedure.Destroy)
                {
                    return RpcMessageCodec.EncodeReply(
                        header.Xid, RpcReplyPayload.Success(ReadOnlyMemory<byte>.Empty), OpaqueAuth.None);
                }

                RpcSecGssServerResult dataResult = _rpcSecGss.ProcessData(header, arguments);
                arguments = dataResult.Payload;
                rpcSecGssContext = dataResult.Context;
                call = call with { RpcSecGss = rpcSecGssContext };
            }
            catch (Exception ex) when (ex is XdrException or RpcException)
            {
                return RpcMessageCodec.EncodeAuthError(header.Xid, AuthStatus.BadCredential);
            }
        }

        RpcReplyPayload payload;
        try
        {
            payload = await program.InvokeAsync(call, arguments, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Any handler failure is reported to the caller as SYSTEM_ERR.
        catch (Exception)
        {
            payload = RpcReplyPayload.SystemError();
        }
#pragma warning restore CA1031

        OpaqueAuth verifier = OpaqueAuth.None;
        if (rpcSecGssContext is not null)
        {
            payload = RpcSecGssServer.ProtectReply(payload, rpcSecGssContext);
            verifier = RpcSecGssServer.CreateReplyVerifier(rpcSecGssContext);
        }

        return RpcMessageCodec.EncodeReply(header.Xid, payload, verifier);
    }
}
