using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>An NFS version 4 file handle (<c>nfs_fh4</c>, RFC 7530): a variable opaque up to 128 bytes.</summary>
[XdrType]
public partial struct Nfs4Handle
{
    /// <summary>The opaque handle bytes.</summary>
    [XdrField(0)]
    [XdrOpaque(Nfs4.MaxHandleSize)]
    public byte[] Data { get; set; }
}

/// <summary>A timestamp with nanosecond resolution (<c>nfstime4</c>, RFC 7530).</summary>
[XdrType]
public partial struct Nfs4Time
{
    /// <summary>Whole seconds since the Unix epoch (signed).</summary>
    [XdrField(0)]
    public long Seconds { get; set; }

    /// <summary>Nanoseconds within the second.</summary>
    [XdrField(1)]
    public uint Nanoseconds { get; set; }
}

/// <summary>Device numbers for a special file (<c>specdata4</c>, RFC 7530).</summary>
[XdrType]
public partial struct Nfs4SpecData
{
    /// <summary>The major device number.</summary>
    [XdrField(0)]
    public uint Major { get; set; }

    /// <summary>The minor device number.</summary>
    [XdrField(1)]
    public uint Minor { get; set; }
}

/// <summary>A file-system identifier (<c>fsid4</c>, RFC 7530).</summary>
[XdrType]
public partial struct Nfs4Fsid
{
    /// <summary>The major identifier.</summary>
    [XdrField(0)]
    public ulong Major { get; set; }

    /// <summary>The minor identifier.</summary>
    [XdrField(1)]
    public ulong Minor { get; set; }
}

/// <summary>A state identifier (<c>stateid4</c>, RFC 7530).</summary>
public record struct Nfs4StateId : IXdrSerializable<Nfs4StateId>
{
    /// <summary>The sequence value.</summary>
    public uint Sequence { get; set; }

    /// <summary>The 12-byte opaque identifier.</summary>
    public byte[] Other { get; set; }

    /// <summary>Gets the special anonymous (all-zero) stateid used for stateless reads.</summary>
    public static Nfs4StateId Anonymous => new() { Sequence = 0, Other = new byte[Nfs4.OtherSize] };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32(Sequence);
        writer.WriteOpaqueFixed(Other ?? new byte[Nfs4.OtherSize]);
    }

    /// <inheritdoc/>
    public static Nfs4StateId ReadFrom(ref XdrReader reader) => new()
    {
        Sequence = reader.ReadUInt32(),
        Other = reader.ReadOpaqueFixed(Nfs4.OtherSize).ToArray(),
    };
}

/// <summary>
/// An NFS version 4 attribute bitmap (<c>bitmap4</c>, RFC 7530): a variable-length array of 32-bit
/// words in which bit <c>n</c> selects attribute <c>n</c>.
/// </summary>
public readonly struct Nfs4Bitmap : IEquatable<Nfs4Bitmap>
{
    private readonly uint[] _words;

    /// <summary>Creates a bitmap from raw 32-bit words.</summary>
    /// <param name="words">The mask words, least significant word first.</param>
    public Nfs4Bitmap(uint[] words) => _words = words ?? [];

    /// <summary>Gets an empty bitmap.</summary>
    public static Nfs4Bitmap Empty => new([]);

    /// <summary>Gets the raw mask words.</summary>
    public uint[] Words => _words ?? [];

    /// <summary>Builds a bitmap that selects the given attributes.</summary>
    /// <param name="attributes">The attributes to select.</param>
    /// <returns>The bitmap.</returns>
    public static Nfs4Bitmap Of(params Nfs4AttributeId[] attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        int maxWord = 0;
        foreach (Nfs4AttributeId attribute in attributes)
        {
            maxWord = Math.Max(maxWord, (int)attribute / 32);
        }

        uint[] words = new uint[maxWord + 1];
        foreach (Nfs4AttributeId attribute in attributes)
        {
            words[(int)attribute / 32] |= 1u << ((int)attribute % 32);
        }

        return new Nfs4Bitmap(words);
    }

    /// <summary>Determines whether the given attribute is selected.</summary>
    /// <param name="attribute">The attribute to test.</param>
    /// <returns><see langword="true"/> if the bit is set; otherwise <see langword="false"/>.</returns>
    public bool IsSet(Nfs4AttributeId attribute)
    {
        uint[] words = _words ?? [];
        int word = (int)attribute / 32;
        return word < words.Length && (words[word] & (1u << ((int)attribute % 32))) != 0;
    }

    /// <summary>Writes the bitmap to <paramref name="writer"/>.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        uint[] words = _words ?? [];
        writer.WriteUInt32((uint)words.Length);
        foreach (uint word in words)
        {
            writer.WriteUInt32(word);
        }
    }

    /// <summary>Reads a bitmap from <paramref name="reader"/>.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The bitmap.</returns>
    public static Nfs4Bitmap ReadFrom(ref XdrReader reader)
    {
        uint count = reader.ReadUInt32();
        if (count > 64)
        {
            throw new XdrException("bitmap4 word count is implausibly large.");
        }

        uint[] words = new uint[count];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt32();
        }

        return new Nfs4Bitmap(words);
    }

    /// <inheritdoc/>
    public bool Equals(Nfs4Bitmap other) => (_words ?? []).AsSpan().SequenceEqual(other._words ?? []);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is Nfs4Bitmap other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (uint word in _words ?? [])
        {
            hash.Add(word);
        }

        return hash.ToHashCode();
    }

    /// <summary>Compares two bitmaps for equality.</summary>
    public static bool operator ==(Nfs4Bitmap left, Nfs4Bitmap right) => left.Equals(right);

    /// <summary>Compares two bitmaps for inequality.</summary>
    public static bool operator !=(Nfs4Bitmap left, Nfs4Bitmap right) => !left.Equals(right);
}
