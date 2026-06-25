namespace Nfs.Xdr;

/// <summary>
/// The XDR <c>void</c> type: it occupies no bytes. Used as the arguments or result of procedures
/// that take or return nothing (for example the RPC NULL procedure).
/// </summary>
public readonly record struct XdrVoid : IXdrSerializable<XdrVoid>
{
    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer)
    {
    }

    /// <inheritdoc/>
    public static XdrVoid ReadFrom(ref XdrReader reader) => default;
}
