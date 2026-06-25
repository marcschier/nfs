namespace Nfs.Xdr;

/// <summary>
/// The exception thrown when XDR-encoded data is malformed, truncated, or exceeds an
/// allowed bound (for example a variable-length item longer than the protocol permits).
/// </summary>
public sealed class XdrException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="XdrException"/> class.</summary>
    public XdrException()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XdrException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    public XdrException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="XdrException"/> class.</summary>
    /// <param name="message">A message describing the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public XdrException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
