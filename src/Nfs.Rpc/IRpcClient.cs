using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// An ONC/RPC client transport. Implementations issue a call and return its reply, hiding whether
/// the underlying transport is TCP (record marking) or UDP (datagrams with retransmission).
/// </summary>
public interface IRpcClient : IAsyncDisposable
{
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
    ValueTask<RpcReply> CallAsync<TArgs>(
        uint program,
        uint version,
        uint procedure,
        OpaqueAuth credential,
        OpaqueAuth verifier,
        TArgs arguments,
        CancellationToken cancellationToken = default)
        where TArgs : IXdrSerializable<TArgs>;
}
