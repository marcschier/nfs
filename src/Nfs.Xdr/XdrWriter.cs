using System.Buffers;
using System.Buffers.Binary;
using System.Text;

namespace Nfs.Xdr;

/// <summary>
/// Writes XDR-encoded (RFC 4506) values, big-endian, into an <see cref="IBufferWriter{T}"/>.
/// </summary>
/// <remarks>
/// Encoding is synchronous: a response is written in full into a pooled buffer and only then
/// sent, so the writer never crosses an <c>await</c> boundary. Variable-length items are
/// followed by zero padding to the next four-byte boundary.
/// </remarks>
public ref struct XdrWriter
{
    private readonly IBufferWriter<byte> _writer;

    /// <summary>Initializes a new <see cref="XdrWriter"/> that appends to the supplied buffer writer.</summary>
    /// <param name="writer">The destination buffer writer.</param>
    public XdrWriter(IBufferWriter<byte> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        _writer = writer;
        BytesWritten = 0;
    }

    /// <summary>Gets the total number of bytes written, including padding.</summary>
    public int BytesWritten { get; private set; }

    /// <summary>Writes a 32-bit signed integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt32(int value)
    {
        BinaryPrimitives.WriteInt32BigEndian(_writer.GetSpan(4), value);
        Advance(4);
    }

    /// <summary>Writes a 32-bit unsigned integer.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(_writer.GetSpan(4), value);
        Advance(4);
    }

    /// <summary>Writes a 64-bit signed integer (an XDR <c>hyper</c>).</summary>
    /// <param name="value">The value to write.</param>
    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64BigEndian(_writer.GetSpan(8), value);
        Advance(8);
    }

    /// <summary>Writes a 64-bit unsigned integer (an XDR <c>unsigned hyper</c>).</summary>
    /// <param name="value">The value to write.</param>
    public void WriteUInt64(ulong value)
    {
        BinaryPrimitives.WriteUInt64BigEndian(_writer.GetSpan(8), value);
        Advance(8);
    }

    /// <summary>Writes a 32-bit IEEE-754 floating-point value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteSingle(float value)
    {
        BinaryPrimitives.WriteSingleBigEndian(_writer.GetSpan(4), value);
        Advance(4);
    }

    /// <summary>Writes a 64-bit IEEE-754 floating-point value.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteDouble(double value)
    {
        BinaryPrimitives.WriteDoubleBigEndian(_writer.GetSpan(8), value);
        Advance(8);
    }

    /// <summary>Writes an XDR boolean as the 32-bit integer 1 (true) or 0 (false).</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBool(bool value) => WriteUInt32(value ? 1u : 0u);

    /// <summary>
    /// Writes a fixed-length opaque value: the bytes verbatim followed by zero padding to the
    /// next four-byte boundary. No length prefix is written.
    /// </summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteOpaqueFixed(ReadOnlySpan<byte> value)
    {
        int padding = XdrConstants.PaddingFor(value.Length);
        Span<byte> span = _writer.GetSpan(value.Length + padding);
        value.CopyTo(span);
        if (padding != 0)
        {
            span.Slice(value.Length, padding).Clear();
        }

        Advance(value.Length + padding);
    }

    /// <summary>
    /// Writes a variable-length opaque value: a 32-bit length prefix, the bytes, then padding.
    /// </summary>
    /// <param name="value">The bytes to write.</param>
    public void WriteOpaqueVariable(ReadOnlySpan<byte> value)
    {
        WriteUInt32((uint)value.Length);
        WriteOpaqueFixed(value);
    }

    /// <summary>Writes a variable-length string using the supplied encoding.</summary>
    /// <param name="value">The string to write.</param>
    /// <param name="encoding">The encoding used to produce the bytes.</param>
    public void WriteString(string value, Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(encoding);

        int byteCount = encoding.GetByteCount(value);
        int padding = XdrConstants.PaddingFor(byteCount);
        WriteUInt32((uint)byteCount);

        Span<byte> span = _writer.GetSpan(byteCount + padding);
        int written = encoding.GetBytes(value, span);
        if (padding != 0)
        {
            span.Slice(written, padding).Clear();
        }

        Advance(written + padding);
    }

    /// <summary>Writes a UTF-8 variable-length string.</summary>
    /// <param name="value">The string to write.</param>
    public void WriteString(string value) => WriteString(value, Encoding.UTF8);

    /// <summary>
    /// Appends bytes verbatim, with no length prefix and no padding. The caller is responsible for
    /// ensuring the bytes already form a sequence of whole XDR items (a multiple of the block size);
    /// this is used to splice pre-encoded payloads, such as procedure results, into a message.
    /// </summary>
    /// <param name="value">The pre-encoded bytes to append.</param>
    public void WriteRaw(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_writer.GetSpan(value.Length));
        Advance(value.Length);
    }

    private void Advance(int count)
    {
        _writer.Advance(count);
        BytesWritten += count;
    }
}
