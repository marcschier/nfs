namespace Nfs.Abstractions;

/// <summary>
/// An opaque NFS file handle: a server-assigned, location-independent identifier for a file-system
/// object. Handles are compared by their bytes and are bounded in size by the protocol.
/// </summary>
public readonly struct NfsFileHandle : IEquatable<NfsFileHandle>
{
    /// <summary>The maximum length, in bytes, of a file handle (NFS3_FHSIZE).</summary>
    public const int MaxLength = 64;

    private readonly byte[]? _bytes;

    /// <summary>Creates a handle that copies the supplied bytes.</summary>
    /// <param name="bytes">The opaque handle bytes (at most <see cref="MaxLength"/>).</param>
    /// <exception cref="ArgumentOutOfRangeException">The handle is longer than <see cref="MaxLength"/>.</exception>
    public NfsFileHandle(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaxLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bytes), bytes.Length, $"A file handle may be at most {MaxLength} bytes.");
        }

        _bytes = bytes.ToArray();
    }

    /// <summary>Gets the number of bytes in the handle.</summary>
    public int Length => _bytes?.Length ?? 0;

    /// <summary>Gets a value indicating whether the handle is empty.</summary>
    public bool IsEmpty => Length == 0;

    /// <summary>Gets a read-only view over the handle bytes.</summary>
    public ReadOnlySpan<byte> Span => _bytes;

    /// <summary>Returns a copy of the handle bytes.</summary>
    /// <returns>A new array containing the handle bytes.</returns>
    public byte[] ToArray() => Span.ToArray();

    /// <inheritdoc/>
    public bool Equals(NfsFileHandle other) => Span.SequenceEqual(other.Span);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NfsFileHandle other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
#if NET6_0_OR_GREATER
        hash.AddBytes(Span);
#else
        // HashCode.AddBytes is net6.0+; on netstandard, fold the bytes in individually.
        ReadOnlySpan<byte> span = Span;
        for (int i = 0; i < span.Length; i++)
        {
            hash.Add(span[i]);
        }
#endif
        return hash.ToHashCode();
    }

    /// <summary>Determines whether two handles have the same bytes.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are equal.</returns>
    public static bool operator ==(NfsFileHandle left, NfsFileHandle right) => left.Equals(right);

    /// <summary>Determines whether two handles have different bytes.</summary>
    /// <param name="left">The first handle.</param>
    /// <param name="right">The second handle.</param>
    /// <returns><see langword="true"/> if the handles are not equal.</returns>
    public static bool operator !=(NfsFileHandle left, NfsFileHandle right) => !left.Equals(right);
}
