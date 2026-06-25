using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// An ONC/RPC <c>opaque_auth</c>: an authentication flavor paired with an opaque body whose
/// meaning depends on the flavor.
/// </summary>
/// <param name="Flavor">The authentication flavor.</param>
/// <param name="Body">The flavor-specific credential or verifier bytes (at most 400 bytes).</param>
public readonly record struct OpaqueAuth(AuthFlavor Flavor, ReadOnlyMemory<byte> Body)
{
    /// <summary>Gets the empty AUTH_NONE credential/verifier.</summary>
    public static OpaqueAuth None => new(AuthFlavor.None, ReadOnlyMemory<byte>.Empty);

    /// <summary>Encodes this value into the supplied writer.</summary>
    /// <param name="writer">The writer to encode into.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Flavor);
        writer.WriteOpaqueVariable(Body.Span);
    }

    /// <summary>Decodes an <see cref="OpaqueAuth"/> from the supplied reader.</summary>
    /// <param name="reader">The reader to decode from.</param>
    /// <returns>The decoded value.</returns>
    public static OpaqueAuth ReadFrom(ref XdrReader reader)
    {
        var flavor = (AuthFlavor)reader.ReadInt32();
        byte[] body = reader.ReadOpaqueVariable(RpcConstants.MaxAuthBodyLength).ToArray();
        return new OpaqueAuth(flavor, body);
    }
}
