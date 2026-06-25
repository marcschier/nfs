using System.Buffers;
using System.IO.Pipelines;

namespace Nfs.Rpc;

/// <summary>
/// Reads and writes complete ONC/RPC messages over a byte stream using the record-marking standard
/// (RFC 5531 §11). A full record is buffered before it is returned, so callers can decode it
/// synchronously with a <c>ref struct</c> reader.
/// </summary>
public static class RpcRecordFraming
{
    /// <summary>The default cap on a single record's assembled size (64 MiB).</summary>
    public const int DefaultMaxRecordLength = 64 * 1024 * 1024;

    /// <summary>Writes <paramref name="message"/> as a single-fragment record and flushes it.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="message">The complete RPC message to frame.</param>
    /// <param name="cancellationToken">A token to cancel the flush.</param>
    public static async ValueTask WriteRecordAsync(
        PipeWriter writer,
        ReadOnlyMemory<byte> message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(writer);

        WriteFragment(writer, message.Span);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one complete record, assembling all of its fragments.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="maxRecordLength">The largest record that will be assembled before failing.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The assembled message bytes.</returns>
    /// <exception cref="RpcException">The stream ended mid-record or the record was too large.</exception>
    public static async ValueTask<byte[]> ReadRecordAsync(
        PipeReader reader,
        int maxRecordLength = DefaultMaxRecordLength,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var assembled = new ArrayBufferWriter<byte>();
        bool lastFragment = false;
        while (!lastFragment)
        {
            (int fragmentLength, bool isLast) = await ReadFragmentHeaderAsync(reader, cancellationToken)
                .ConfigureAwait(false);
            lastFragment = isLast;

            if ((long)assembled.WrittenCount + fragmentLength > maxRecordLength)
            {
                throw new RpcException($"RPC record exceeds the maximum of {maxRecordLength} bytes.");
            }

            await ReadFragmentPayloadAsync(reader, fragmentLength, assembled, cancellationToken)
                .ConfigureAwait(false);
        }

        return assembled.WrittenSpan.ToArray();
    }

    private static void WriteFragment(PipeWriter writer, ReadOnlySpan<byte> message)
    {
        RecordMarking.WriteHeader(writer.GetSpan(RecordMarking.HeaderSize), message.Length, isLastFragment: true);
        writer.Advance(RecordMarking.HeaderSize);
        writer.Write(message);
    }

    private static async ValueTask<(int Length, bool IsLast)> ReadFragmentHeaderAsync(
        PipeReader reader,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.Length >= RecordMarking.HeaderSize)
            {
                (int length, bool isLast) = ParseHeader(buffer);
                reader.AdvanceTo(buffer.GetPosition(RecordMarking.HeaderSize));
                return (length, isLast);
            }

            if (result.IsCompleted)
            {
                throw new RpcException(buffer.IsEmpty
                    ? "The connection was closed."
                    : "The connection closed in the middle of a record header.");
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        }
    }

    private static async ValueTask ReadFragmentPayloadAsync(
        PipeReader reader,
        int length,
        ArrayBufferWriter<byte> destination,
        CancellationToken cancellationToken)
    {
        int remaining = length;
        while (remaining > 0)
        {
            ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.IsEmpty && result.IsCompleted)
            {
                throw new RpcException("The connection closed in the middle of a record.");
            }

            int toCopy = (int)Math.Min(remaining, buffer.Length);
            CopyTo(buffer.Slice(0, toCopy), destination);
            remaining -= toCopy;
            reader.AdvanceTo(buffer.GetPosition(toCopy));
        }
    }

    private static (int Length, bool IsLast) ParseHeader(ReadOnlySequence<byte> buffer)
    {
        Span<byte> header = stackalloc byte[RecordMarking.HeaderSize];
        buffer.Slice(0, RecordMarking.HeaderSize).CopyTo(header);
        return RecordMarking.ParseHeader(header);
    }

    private static void CopyTo(ReadOnlySequence<byte> source, ArrayBufferWriter<byte> destination)
    {
        foreach (ReadOnlyMemory<byte> segment in source)
        {
            destination.Write(segment.Span);
        }
    }
}
