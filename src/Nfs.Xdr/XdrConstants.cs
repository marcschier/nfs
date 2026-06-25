namespace Nfs.Xdr;

/// <summary>
/// Constants defined by the External Data Representation (XDR) standard, RFC 4506.
/// </summary>
public static class XdrConstants
{
    /// <summary>
    /// The XDR block size, in bytes. Every encoded item is padded with zero bytes up to
    /// the next multiple of this value, so all items start on a four-byte boundary.
    /// </summary>
    public const int BlockSize = 4;

    /// <summary>
    /// Returns the number of zero padding bytes that follow an item of the given length so
    /// that the next item starts on an XDR block boundary.
    /// </summary>
    /// <param name="length">The length, in bytes, of the unpadded item.</param>
    /// <returns>A value in the range 0..3.</returns>
    public static int PaddingFor(int length) => (-length) & (BlockSize - 1);
}
