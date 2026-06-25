using System.Net;
using System.Net.Sockets;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A minimal ONC/RPC client over UDP. Each call is sent as a single datagram and its reply is
/// awaited, with retransmission on timeout. Calls on a single instance are serialized.
/// </summary>
public sealed class RpcUdpClient : IRpcClient
{
    private const int MaxDatagramSize = 65535;

    private readonly Socket _socket;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _initialTimeout;
    private readonly int _maxRetries;
    private uint _xid;
    private bool _disposed;

    private RpcUdpClient(Socket socket, TimeSpan initialTimeout, int maxRetries)
    {
        _socket = socket;
        _initialTimeout = initialTimeout;
        _maxRetries = maxRetries;
        _xid = (uint)Random.Shared.Next();
    }

    /// <summary>Connects (associates) a UDP socket to an RPC server endpoint.</summary>
    /// <param name="endPoint">The server endpoint.</param>
    /// <param name="initialTimeout">The first retransmission timeout (doubled on each retry).</param>
    /// <param name="maxRetries">The number of retransmissions before giving up.</param>
    /// <param name="cancellationToken">A token to cancel the connect.</param>
    /// <returns>A connected client.</returns>
    public static async ValueTask<RpcUdpClient> ConnectAsync(
        EndPoint endPoint,
        TimeSpan? initialTimeout = null,
        int maxRetries = 4,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endPoint);

        var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        try
        {
            await socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            socket.Dispose();
            throw;
        }

        return new RpcUdpClient(socket, initialTimeout ?? TimeSpan.FromSeconds(1), maxRetries);
    }

    /// <inheritdoc/>
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
            uint xid = unchecked(_xid++);
            byte[] message = RpcMessageCodec.EncodeCall(
                xid, program, version, procedure, credential, verifier, arguments);

            byte[] buffer = new byte[MaxDatagramSize];
            TimeSpan timeout = _initialTimeout;

            for (int attempt = 0; attempt <= _maxRetries; attempt++)
            {
                await _socket.SendAsync(message, SocketFlags.None, cancellationToken).ConfigureAwait(false);

                byte[]? reply = await ReceiveMatchingAsync(buffer, xid, timeout, cancellationToken)
                    .ConfigureAwait(false);
                if (reply is not null)
                {
                    return RpcMessageCodec.ParseReply(xid, reply);
                }

                timeout += timeout; // Exponential back-off between retransmissions.
            }

            throw new RpcException($"No UDP reply received for XID {xid} after {_maxRetries + 1} attempts.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _socket.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<byte[]?> ReceiveMatchingAsync(
        byte[] buffer,
        uint xid,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(timeout);

        while (true)
        {
            int received;
            try
            {
                received = await _socket.ReceiveAsync(buffer, SocketFlags.None, attempt.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null; // This attempt timed out; the caller will retransmit.
            }

            if (received >= 4 && RpcMessageCodec.PeekReplyXid(buffer.AsSpan(0, received)) == xid)
            {
                return buffer.AsSpan(0, received).ToArray();
            }

            // A stale reply from an earlier retransmission: ignore it and keep waiting.
        }
    }
}
