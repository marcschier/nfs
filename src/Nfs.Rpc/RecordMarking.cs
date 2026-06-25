using System.Buffers.Binary;

namespace Nfs.Rpc;

/// <summary>
/// The ONC/RPC record-marking standard (RFC 5531 §11), used to delimit messages on a byte stream
/// such as TCP. A record is a sequence of fragments; each fragment is preceded by a four-byte,
/// big-endian header whose most significant bit marks the final fragment of the record and whose
/// low 31 bits give the number of bytes in the fragment that follow the header.
/// </summary>
public static class RecordMarking
{
    /// <summary>The size, in bytes, of a fragment header.</summary>
    public const int HeaderSize = 4;

    /// <summary>The largest length a single fragment may declare (2^31 - 1).</summary>
    public const int MaxFragmentLength = 0x7FFF_FFFF;

    private const uint LastFragmentFlag = 0x8000_0000u;

    /// <summary>Writes a fragment header into the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">A buffer of at least <see cref="HeaderSize"/> bytes.</param>
    /// <param name="fragmentLength">The number of payload bytes the fragment will contain.</param>
    /// <param name="isLastFragment">Whether this is the final fragment of the record.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="fragmentLength"/> is negative or exceeds <see cref="MaxFragmentLength"/>.
    /// </exception>
    public static void WriteHeader(Span<byte> destination, int fragmentLength, bool isLastFragment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fragmentLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fragmentLength, MaxFragmentLength);

        uint marker = (uint)fragmentLength;
        if (isLastFragment)
        {
            marker |= LastFragmentFlag;
        }

        BinaryPrimitives.WriteUInt32BigEndian(destination, marker);
    }

    /// <summary>Parses a fragment header from the start of <paramref name="source"/>.</summary>
    /// <param name="source">A buffer of at least <see cref="HeaderSize"/> bytes.</param>
    /// <returns>The fragment's payload length and whether it is the final fragment.</returns>
    public static (int FragmentLength, bool IsLastFragment) ParseHeader(ReadOnlySpan<byte> source)
    {
        uint marker = BinaryPrimitives.ReadUInt32BigEndian(source);
        int length = (int)(marker & ~LastFragmentFlag);
        bool isLast = (marker & LastFragmentFlag) != 0;
        return (length, isLast);
    }
}
