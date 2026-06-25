using System.Net;
using System.Net.Sockets;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A minimal ONC/RPC server over UDP. Each datagram is one complete call message; the reply is sent
/// back to the sender as a single datagram. A duplicate-request cache replays the reply to
/// retransmissions so that non-idempotent procedures run only once.
/// </summary>
public sealed class RpcUdpServer : IAsyncDisposable
{
    private const int MaxDatagramSize = 65535;

    private readonly Socket _socket;
    private readonly Dictionary<uint, IRpcProgram> _programs;
    private readonly RpcDuplicateRequestCache _cache = new();
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _receiveLoop;
    private bool _disposed;

    /// <summary>Creates a server bound to <paramref name="endPoint"/> serving <paramref name="program"/>.</summary>
    /// <param name="endPoint">The local endpoint to bind to.</param>
    /// <param name="program">The program to dispatch calls to.</param>
    public RpcUdpServer(IPEndPoint endPoint, IRpcProgram program)
        : this(endPoint, new[] { program })
    {
    }

    /// <summary>Creates a server bound to <paramref name="endPoint"/> serving several programs.</summary>
    /// <param name="endPoint">The local endpoint to bind to.</param>
    /// <param name="programs">The programs to dispatch calls to, keyed by program number.</param>
    public RpcUdpServer(IPEndPoint endPoint, IEnumerable<IRpcProgram> programs)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        ArgumentNullException.ThrowIfNull(programs);

        _programs = new Dictionary<uint, IRpcProgram>();
        foreach (IRpcProgram program in programs)
        {
            ArgumentNullException.ThrowIfNull(program);
            _programs[program.Program] = program;
        }

        _socket = new Socket(endPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(endPoint);
    }

    /// <summary>Gets the endpoint the server is bound to (useful when binding to port 0).</summary>
    public IPEndPoint LocalEndPoint => (IPEndPoint)_socket.LocalEndPoint!;

    /// <summary>Starts receiving datagrams in the background.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _receiveLoop ??= Task.Run(() => ReceiveLoopAsync(_shutdown.Token));
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
        _socket.Dispose();

        if (_receiveLoop is not null)
        {
            try
            {
                await _receiveLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }

        _shutdown.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaxDatagramSize];
        var any = new IPEndPoint(_socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            SocketReceiveFromResult received;
            try
            {
                received = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, any, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                continue; // A transient datagram error (e.g., ICMP port unreachable) must not stop the server.
            }

            byte[] message = buffer.AsSpan(0, received.ReceivedBytes).ToArray();
            _ = HandleDatagramAsync(message, (IPEndPoint)received.RemoteEndPoint, cancellationToken);
        }
    }

    private async Task HandleDatagramAsync(byte[] message, IPEndPoint sender, CancellationToken cancellationToken)
    {
        RpcCallHeader header;
        int argumentsOffset;
        try
        {
            (header, argumentsOffset) = RpcMessageCodec.ParseCallHeader(message);
        }
        catch (Exception ex) when (ex is XdrException or RpcException)
        {
            return; // Cannot correlate a reply for an unparseable call; drop it.
        }

        var key = new RpcDuplicateRequestCache.Key(
            sender.ToString(), header.Xid, header.Program, header.Version, header.Procedure);

        byte[] reply = await _cache
            .GetOrStart(key, () => ComputeReplyAsync(header, message, argumentsOffset, cancellationToken))
            .ConfigureAwait(false);

        try
        {
            await _socket.SendToAsync(reply, SocketFlags.None, sender, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The client is gone or the server is shutting down; nothing to do.
        }
    }

    private async Task<byte[]> ComputeReplyAsync(
        RpcCallHeader header,
        byte[] message,
        int argumentsOffset,
        CancellationToken cancellationToken)
    {
        if (!_programs.TryGetValue(header.Program, out IRpcProgram? program))
        {
            return RpcMessageCodec.EncodeReply(header.Xid, RpcReplyPayload.ProgramUnavailable());
        }

        var arguments = new ReadOnlyMemory<byte>(message, argumentsOffset, message.Length - argumentsOffset);
        var call = new RpcCallInfo(header.Xid, header.Version, header.Procedure, header.Credential, header.Verifier);

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

        return RpcMessageCodec.EncodeReply(header.Xid, payload);
    }
}
