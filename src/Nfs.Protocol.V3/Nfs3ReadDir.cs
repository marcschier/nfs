using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>A single directory entry (<c>entry3</c>, RFC 1813).</summary>
/// <param name="FileId">The object's unique identifier within the file system.</param>
/// <param name="Name">The entry name.</param>
/// <param name="Cookie">An opaque continuation token positioned after this entry.</param>
public readonly record struct Nfs3DirEntry(ulong FileId, string Name, ulong Cookie);

/// <summary>Arguments for READDIR (<c>READDIR3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadDirArgs
{
    /// <summary>The directory handle.</summary>
    [XdrField(0)]
    public Nfs3Handle Directory { get; set; }

    /// <summary>The continuation cookie (0 to start at the beginning).</summary>
    [XdrField(1)]
    public ulong Cookie { get; set; }

    /// <summary>The cookie verifier from a previous read (8 bytes).</summary>
    [XdrField(2)]
    [XdrFixedOpaque(8)]
    public byte[] CookieVerifier { get; set; }

    /// <summary>A hint for the maximum number of bytes of entries to return.</summary>
    [XdrField(3)]
    public uint Count { get; set; }
}

/// <summary>The failure arm of READDIR (<c>READDIR3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadDirResultFail
{
    /// <summary>The directory's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }
}

/// <summary>
/// The success arm of READDIR (<c>READDIR3resok</c>, RFC 1813), including the <c>dirlist3</c> entry
/// list, which is encoded as a sequence of present-flag/entry pairs terminated by a false flag.
/// </summary>
public record struct Nfs3ReadDirResultOk : IXdrSerializable<Nfs3ReadDirResultOk>
{
    /// <summary>The directory's attributes, if available.</summary>
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }

    /// <summary>The cookie verifier for this listing (8 bytes).</summary>
    public byte[] CookieVerifier { get; set; }

    /// <summary>The directory entries.</summary>
    public Nfs3DirEntry[] Entries { get; set; }

    /// <summary>Whether the end of the directory was reached.</summary>
    public bool Eof { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        if (DirectoryAttributes.HasValue)
        {
            writer.WriteBool(true);
            DirectoryAttributes.Value.WriteTo(ref writer);
        }
        else
        {
            writer.WriteBool(false);
        }

        writer.WriteOpaqueFixed(CookieVerifier ?? new byte[8]);

        foreach (Nfs3DirEntry entry in Entries ?? [])
        {
            writer.WriteBool(true);
            writer.WriteUInt64(entry.FileId);
            writer.WriteString(entry.Name);
            writer.WriteUInt64(entry.Cookie);
        }

        writer.WriteBool(false);
        writer.WriteBool(Eof);
    }

    /// <inheritdoc/>
    public static Nfs3ReadDirResultOk ReadFrom(ref XdrReader reader)
    {
        Nfs3FileAttributes? attributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
        byte[] cookieVerifier = reader.ReadOpaqueFixed(8).ToArray();

        var entries = new List<Nfs3DirEntry>();
        while (reader.ReadBool())
        {
            ulong fileId = reader.ReadUInt64();
            string name = reader.ReadString(255);
            ulong cookie = reader.ReadUInt64();
            entries.Add(new Nfs3DirEntry(fileId, name, cookie));
        }

        bool eof = reader.ReadBool();
        return new Nfs3ReadDirResultOk
        {
            DirectoryAttributes = attributes,
            CookieVerifier = cookieVerifier,
            Entries = [.. entries],
            Eof = eof,
        };
    }
}

/// <summary>The result of READDIR (<c>READDIR3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3ReadDirResult : IXdrSerializable<Nfs3ReadDirResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3ReadDirResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3ReadDirResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadDirResult Success(Nfs3ReadDirResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadDirResult Failure(NfsStatus status, Nfs3ReadDirResultFail fail = default) =>
        new() { Status = status, Fail = fail };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Ok.WriteTo(ref writer);
        }
        else
        {
            Fail.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3ReadDirResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3ReadDirResult { Status = status, Ok = Nfs3ReadDirResultOk.ReadFrom(ref reader) }
            : new Nfs3ReadDirResult { Status = status, Fail = Nfs3ReadDirResultFail.ReadFrom(ref reader) };
    }
}
