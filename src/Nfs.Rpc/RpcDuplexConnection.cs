using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net.Sockets;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A bidirectional ONC/RPC TCP connection that demultiplexes replies by XID and dispatches inbound calls.
/// </summary>
public sealed class RpcDuplexConnection : IAsyncDisposable
{
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;
    private readonly Func<RpcDuplexConnection, byte[], CancellationToken, ValueTask<byte[]?>> _callHandler;
    private readonly ConcurrentDictionary<uint, PendingCall> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private int _nextXid = Random.Shared.Next();
    private bool _disposed;

    /// <summary>Creates a duplex connection over an established TCP stream.</summary>
    /// <param name="stream">The network stream. It is owned by the connection.</param>
    /// <param name="callHandler">Dispatches inbound CALL records and returns their complete RPC reply.</param>
    public RpcDuplexConnection(
        NetworkStream stream,
        Func<RpcDuplexConnection, byte[], CancellationToken, ValueTask<byte[]?>> callHandler)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(callHandler);

        _stream = stream;
        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);
        _callHandler = callHandler;
        Completion = Task.Run(() => ReadLoopAsync(_shutdown.Token));
    }

    /// <summary>Gets a task that completes when the connection read loop exits.</summary>
    public Task Completion { get; }

    /// <summary>Invokes a remote procedure and awaits the matching reply by XID.</summary>
    /// <typeparam name="TArgs">The procedure argument type.</typeparam>
    /// <param name="program">The remote program number.</param>
    /// <param name="version">The remote program version.</param>
    /// <param name="procedure">The procedure number.</param>
    /// <param name="credential">The caller's credential.</param>
    /// <param name="verifier">The caller's verifier.</param>
    /// <param name="arguments">The procedure arguments.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The decoded reply.</returns>
    public ValueTask<RpcReply> CallAsync<TArgs>(
        uint program,
        uint version,
        uint procedure,
        OpaqueAuth credential,
        OpaqueAuth verifier,
        TArgs arguments,
        CancellationToken cancellationToken = default)
        where TArgs : IXdrSerializable<TArgs>
    {
        uint xid = NextXid();
        byte[] message = RpcMessageCodec.EncodeCall(
            xid, program, version, procedure, credential, verifier, arguments);
        return CallEncodedAsync(xid, message, cancellationToken);
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

        uint xid = NextXid();
        RpcSecGssClientCall call = rpcSecGss.CreateCall(
            xid, program, version, procedure, service, arguments);
        byte[] message = RpcMessageCodec.EncodeCall(
            xid, program, version, procedure, call.Credential, call.Verifier, call.Arguments);
        RpcReply reply = await CallEncodedAsync(xid, message, cancellationToken).ConfigureAwait(false);
        return rpcSecGss.DecodeReply(reply, call.SequenceNumber, call.Service);
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
        await _reader.CompleteAsync().ConfigureAwait(false);
        await _writer.CompleteAsync().ConfigureAwait(false);
        await _stream.DisposeAsync().ConfigureAwait(false);

        try
        {
            await Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (RpcException)
        {
        }

        _shutdown.Dispose();
        _writeGate.Dispose();
    }

    private async ValueTask<RpcReply> CallEncodedAsync(
        uint xid,
        byte[] message,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var pending = new PendingCall(cancellationToken);
        if (!_pending.TryAdd(xid, pending))
        {
            throw new RpcException($"Duplicate RPC XID {xid}.");
        }

        try
        {
            await WriteRecordAsync(message, cancellationToken).ConfigureAwait(false);
            byte[] reply = await pending.Task.ConfigureAwait(false);
            return RpcMessageCodec.ParseReply(xid, reply);
        }
        finally
        {
            _pending.TryRemove(xid, out _);
            pending.Dispose();
        }
    }

    private uint NextXid() => unchecked((uint)Interlocked.Increment(ref _nextXid));

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] message = await RpcRecordFraming
                    .ReadRecordAsync(_reader, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                MessageType type = RpcMessageCodec.PeekMessageType(message);
                if (type == MessageType.Reply)
                {
                    CompleteReply(message);
                }
                else if (type == MessageType.Call)
                {
                    _ = ProcessInboundCallAsync(message, cancellationToken);
                }
                else
                {
                    throw new RpcException($"Unknown RPC message type {(int)type}.");
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            FailPending(ex);
        }
        catch (Exception ex)
        {
            FailPending(ex);
        }
    }

    private void CompleteReply(byte[] message)
    {
        uint xid = RpcMessageCodec.PeekReplyXid(message);
        if (_pending.TryGetValue(xid, out PendingCall? pending))
        {
            pending.TrySetResult(message);
        }
    }

    private async Task ProcessInboundCallAsync(byte[] message, CancellationToken cancellationToken)
    {
        byte[]? reply = await _callHandler(this, message, cancellationToken).ConfigureAwait(false);
        if (reply is not null)
        {
            await WriteRecordAsync(reply, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask WriteRecordAsync(byte[] message, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RpcRecordFraming.WriteRecordAsync(_writer, message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void FailPending(Exception exception)
    {
        foreach (PendingCall pending in _pending.Values)
        {
            pending.TrySetException(exception);
        }
    }

    private sealed class PendingCall : IDisposable
    {
        private readonly TaskCompletionSource<byte[]> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public PendingCall(CancellationToken cancellationToken)
        {
            if (cancellationToken.CanBeCanceled)
            {
                _registration = cancellationToken.Register(static state =>
                {
                    var pending = (PendingCall)state!;
                    pending._completion.TrySetCanceled();
                }, this);
            }
        }

        public Task<byte[]> Task => _completion.Task;

        public void TrySetResult(byte[] reply) => _completion.TrySetResult(reply);

        public void TrySetException(Exception exception) => _completion.TrySetException(exception);

        public void Dispose() => _registration.Dispose();
    }
}
