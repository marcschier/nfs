namespace Nfs.Xdr;

/// <summary>
/// Implemented by types that can encode themselves to, and decode themselves from, XDR.
/// </summary>
/// <typeparam name="TSelf">The implementing type itself.</typeparam>
/// <remarks>
/// The <see cref="ReadFrom"/> factory is a <see langword="static"/> <see langword="abstract"/>
/// member so that generic, monomorphized (de)serialization can be expressed without runtime
/// reflection, keeping the stack NativeAOT-safe. Codecs are normally produced by the XDR source
/// generator; the interface is the contract its output implements.
/// </remarks>
public interface IXdrSerializable<TSelf>
    where TSelf : IXdrSerializable<TSelf>
{
    /// <summary>Encodes this value into the supplied writer.</summary>
    /// <param name="writer">The writer to encode into.</param>
    void WriteTo(ref XdrWriter writer);

    /// <summary>Decodes a value of type <typeparamref name="TSelf"/> from the supplied reader.</summary>
    /// <param name="reader">The reader to decode from.</param>
    /// <returns>The decoded value.</returns>
    static abstract TSelf ReadFrom(ref XdrReader reader);
}
