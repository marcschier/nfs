using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A decoded ONC/RPC reply: its header plus the undecoded, procedure-specific result bytes.
/// </summary>
/// <param name="Header">The reply header.</param>
/// <param name="Result">The procedure results (present only when the call succeeded).</param>
public readonly record struct RpcReply(RpcReplyHeader Header, ReadOnlyMemory<byte> Result)
{
    /// <summary>Gets a value indicating whether the call was accepted and completed successfully.</summary>
    public bool IsSuccess => Header.IsSuccess;

    /// <summary>Decodes the procedure results as a value of type <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <returns>The decoded results.</returns>
    /// <exception cref="RpcException">The call did not complete successfully.</exception>
    public T DecodeResult<T>()
        where T : IXdrSerializable<T>
    {
        if (!IsSuccess)
        {
            throw new RpcException(
                $"Cannot decode results: the call was not successful (status {Header.Status}/{Header.Accept}).");
        }

        var reader = new XdrReader(Result.Span);
#if NET7_0_OR_GREATER
        return T.ReadFrom(ref reader);
#else
        return XdrDecoder.ReadFrom<T>(ref reader);
#endif
    }
}
