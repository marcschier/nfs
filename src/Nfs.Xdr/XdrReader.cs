using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Nfs.Xdr;

/// <summary>
/// Reads XDR-encoded (RFC 4506) values from a contiguous, big-endian buffer.
/// </summary>
/// <remarks>
/// This is the contiguous fast path. The RPC layer buffers a complete record before decoding,
/// so the reader operates synchronously over a single <see cref="ReadOnlySpan{T}"/> and never
/// crosses an <c>await</c> boundary. Variable-length reads take an explicit maximum so a hostile
/// or corrupt peer cannot force an unbounded allocation.
/// </remarks>
public ref struct XdrReader
{
    private readonly ReadOnlySpan<byte> _buffer;

    /// <summary>Initializes a new <see cref="XdrReader"/> over the supplied buffer.</summary>
    /// <param name="buffer">The XDR-encoded data to read.</param>
    public XdrReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }

    /// <summary>Gets the number of bytes consumed from the buffer so far.</summary>
    public int Position { get; private set; }

    /// <summary>Gets the number of bytes that remain to be read.</summary>
    public readonly int Remaining => _buffer.Length - Position;

    /// <summary>Reads a 32-bit signed integer.</summary>
    /// <returns>The decoded value.</returns>
    public int ReadInt32() => BinaryPrimitives.ReadInt32BigEndian(Consume(4));

    /// <summary>Reads a 32-bit unsigned integer.</summary>
    /// <returns>The decoded value.</returns>
    public uint ReadUInt32() => BinaryPrimitives.ReadUInt32BigEndian(Consume(4));

    /// <summary>Reads a 64-bit signed integer (an XDR <c>hyper</c>).</summary>
    /// <returns>The decoded value.</returns>
    public long ReadInt64() => BinaryPrimitives.ReadInt64BigEndian(Consume(8));

    /// <summary>Reads a 64-bit unsigned integer (an XDR <c>unsigned hyper</c>).</summary>
    /// <returns>The decoded value.</returns>
    public ulong ReadUInt64() => BinaryPrimitives.ReadUInt64BigEndian(Consume(8));

    /// <summary>Reads a 32-bit IEEE-754 floating-point value.</summary>
    /// <returns>The decoded value.</returns>
    public float ReadSingle() => BinaryPrimitives.ReadSingleBigEndian(Consume(4));

    /// <summary>Reads a 64-bit IEEE-754 floating-point value.</summary>
    /// <returns>The decoded value.</returns>
    public double ReadDouble() => BinaryPrimitives.ReadDoubleBigEndian(Consume(8));

    /// <summary>Reads an XDR boolean, which is encoded as a 32-bit integer that must be 0 or 1.</summary>
    /// <returns>The decoded value.</returns>
    /// <exception cref="XdrException">The encoded value is neither 0 nor 1.</exception>
    public bool ReadBool()
    {
        uint value = ReadUInt32();
        return value switch
        {
            0 => false,
            1 => true,
            _ => ThrowInvalidBool(value),
        };
    }

    /// <summary>
    /// Reads the 32-bit length prefix of a variable-length item and validates it against a maximum.
    /// </summary>
    /// <param name="maxLength">The largest length the protocol permits.</param>
    /// <returns>The decoded length.</returns>
    /// <exception cref="XdrException">The encoded length exceeds <paramref name="maxLength"/>.</exception>
    public int ReadLength(int maxLength)
    {
        uint length = ReadUInt32();
        if (length > (uint)maxLength)
        {
            ThrowLengthExceeded(length, maxLength);
        }

        return (int)length;
    }

    /// <summary>
    /// Reads a fixed-length opaque value of exactly <paramref name="length"/> bytes, skipping any
    /// trailing XDR padding.
    /// </summary>
    /// <param name="length">The number of bytes to read.</param>
    /// <returns>A view over the bytes within the source buffer.</returns>
    /// <remarks>The returned span aliases the source buffer; copy it if it must outlive the reader.</remarks>
    public ReadOnlySpan<byte> ReadOpaqueFixed(int length)
    {
        ReadOnlySpan<byte> data = Consume(length);
        SkipPadding(length);
        return data;
    }

    /// <summary>
    /// Reads a variable-length opaque value (a 32-bit length followed by that many bytes and padding).
    /// </summary>
    /// <param name="maxLength">The largest length the protocol permits.</param>
    /// <returns>A view over the bytes within the source buffer.</returns>
    /// <remarks>The returned span aliases the source buffer; copy it if it must outlive the reader.</remarks>
    public ReadOnlySpan<byte> ReadOpaqueVariable(int maxLength)
    {
        int length = ReadLength(maxLength);
        ReadOnlySpan<byte> data = Consume(length);
        SkipPadding(length);
        return data;
    }

    /// <summary>Reads a variable-length string using the supplied encoding.</summary>
    /// <param name="maxLength">The largest encoded byte length the protocol permits.</param>
    /// <param name="encoding">The encoding used to decode the bytes.</param>
    /// <returns>The decoded string.</returns>
    public string ReadString(int maxLength, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        return encoding.GetString(ReadOpaqueVariable(maxLength));
    }

    /// <summary>Reads a UTF-8 variable-length string.</summary>
    /// <param name="maxLength">The largest encoded byte length the protocol permits.</param>
    /// <returns>The decoded string.</returns>
    public string ReadString(int maxLength) => ReadString(maxLength, Encoding.UTF8);

    private ReadOnlySpan<byte> Consume(int count)
    {
        if ((uint)count > (uint)(_buffer.Length - Position))
        {
            ThrowEndOfBuffer(count);
        }

        ReadOnlySpan<byte> slice = _buffer.Slice(Position, count);
        Position += count;
        return slice;
    }

    private void SkipPadding(int length)
    {
        int padding = XdrConstants.PaddingFor(length);
        if (padding != 0)
        {
            _ = Consume(padding);
        }
    }

    [DoesNotReturn]
    private readonly void ThrowEndOfBuffer(int count) =>
        throw new XdrException(
            $"Attempted to read {count} byte(s) at offset {Position} but only {Remaining} remain.");

    [DoesNotReturn]
    private static bool ThrowInvalidBool(uint value) =>
        throw new XdrException($"Invalid XDR boolean: expected 0 or 1 but read {value}.");

    [DoesNotReturn]
    private static void ThrowLengthExceeded(uint length, int maxLength) =>
        throw new XdrException($"Encoded length {length} exceeds the maximum of {maxLength}.");
}
