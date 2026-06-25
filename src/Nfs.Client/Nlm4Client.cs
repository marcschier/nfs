using Nfs.Nlm;
using Nfs.Rpc;
using Nfs.Xdr;

namespace Nfs.Client;

/// <summary>
/// A typed Network Lock Manager (NLM version 4) client. It wraps a connected <see cref="IRpcClient"/>
/// and exposes the advisory byte-range lock operations used alongside NFS v2/v3.
/// </summary>
public sealed class Nlm4Client
{
    private readonly IRpcClient _rpc;
    private readonly OpaqueAuth _credential;
    private int _cookie;

    /// <summary>Creates a client that issues calls over <paramref name="rpc"/>.</summary>
    /// <param name="rpc">A connected RPC client.</param>
    /// <param name="credential">The credential to attach to each call (defaults to AUTH_NONE).</param>
    public Nlm4Client(IRpcClient rpc, OpaqueAuth credential = default)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _credential = credential;
    }

    /// <summary>Calls the NULL procedure, which does nothing but exercise the connection.</summary>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>A task that completes when the server replies.</returns>
    public async ValueTask NullAsync(CancellationToken cancellationToken = default)
    {
        RpcReply reply = await _rpc.CallAsync(
            Nlm4.Program,
            Nlm4.ProtocolVersion,
            (uint)Nlm4Procedure.Null,
            _credential,
            OpaqueAuth.None,
            default(XdrVoid),
            cancellationToken).ConfigureAwait(false);

        EnsureAccepted(reply);
    }

    /// <summary>Tests whether a lock could be granted.</summary>
    /// <param name="lock">The lock to test.</param>
    /// <param name="exclusive">Whether an exclusive lock is tested.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The TEST result.</returns>
    public ValueTask<Nlm4TestRes> TestAsync(
        Nlm4Lock @lock,
        bool exclusive,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nlm4TestArgs, Nlm4TestRes>(
            Nlm4Procedure.Test,
            new Nlm4TestArgs { Cookie = NextCookie(), Exclusive = exclusive, Lock = @lock },
            cancellationToken);

    /// <summary>Acquires a lock.</summary>
    /// <param name="lock">The lock to acquire.</param>
    /// <param name="exclusive">Whether an exclusive lock is requested.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LOCK result.</returns>
    public ValueTask<Nlm4Res> LockAsync(
        Nlm4Lock @lock,
        bool exclusive,
        CancellationToken cancellationToken = default) =>
        LockAsync(@lock, exclusive, block: false, cancellationToken);

    /// <summary>Acquires a lock, optionally blocking on the server.</summary>
    /// <param name="lock">The lock to acquire.</param>
    /// <param name="exclusive">Whether an exclusive lock is requested.</param>
    /// <param name="block">Whether the server should queue the request if it conflicts.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The LOCK result.</returns>
    public ValueTask<Nlm4Res> LockAsync(
        Nlm4Lock @lock,
        bool exclusive,
        bool block,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nlm4LockArgs, Nlm4Res>(
            Nlm4Procedure.Lock,
            new Nlm4LockArgs
            {
                Cookie = NextCookie(),
                Block = block,
                Exclusive = exclusive,
                Lock = @lock,
                Reclaim = false,
                State = 0,
            },
            cancellationToken);

    /// <summary>Releases a lock.</summary>
    /// <param name="lock">The lock to release.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The UNLOCK result.</returns>
    public ValueTask<Nlm4Res> UnlockAsync(Nlm4Lock @lock, CancellationToken cancellationToken = default) =>
        CallAsync<Nlm4UnlockArgs, Nlm4Res>(
            Nlm4Procedure.Unlock,
            new Nlm4UnlockArgs { Cookie = NextCookie(), Lock = @lock },
            cancellationToken);

    /// <summary>Cancels a pending blocking lock request.</summary>
    /// <param name="lock">The lock request to cancel.</param>
    /// <param name="exclusive">Whether the request was exclusive.</param>
    /// <param name="cancellationToken">A token to cancel the call.</param>
    /// <returns>The CANCEL result.</returns>
    public ValueTask<Nlm4Res> CancelAsync(
        Nlm4Lock @lock,
        bool exclusive,
        CancellationToken cancellationToken = default) =>
        CallAsync<Nlm4CancelArgs, Nlm4Res>(
            Nlm4Procedure.Cancel,
            new Nlm4CancelArgs { Cookie = NextCookie(), Block = true, Exclusive = exclusive, Lock = @lock },
            cancellationToken);

    private byte[] NextCookie()
    {
        int value = Interlocked.Increment(ref _cookie);
        return
        [
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        ];
    }

    private async ValueTask<TResult> CallAsync<TArgs, TResult>(
        Nlm4Procedure procedure,
        TArgs arguments,
        CancellationToken cancellationToken)
        where TArgs : IXdrSerializable<TArgs>
        where TResult : IXdrSerializable<TResult>
    {
        RpcReply reply = await _rpc.CallAsync(
            Nlm4.Program,
            Nlm4.ProtocolVersion,
            (uint)procedure,
            _credential,
            OpaqueAuth.None,
            arguments,
            cancellationToken).ConfigureAwait(false);

        EnsureAccepted(reply);
        return reply.DecodeResult<TResult>();
    }

    private static void EnsureAccepted(RpcReply reply)
    {
        if (!reply.IsSuccess)
        {
            throw new RpcException(
                $"The NLM call was not accepted (reply {reply.Header.Status}/{reply.Header.Accept}).");
        }
    }
}
