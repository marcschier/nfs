using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>A single directory entry with attributes and a handle (<c>entryplus3</c>, RFC 1813).</summary>
/// <param name="FileId">The object's unique identifier within the file system.</param>
/// <param name="Name">The entry name.</param>
/// <param name="Cookie">An opaque continuation token positioned after this entry.</param>
/// <param name="Attributes">The entry's attributes, if the server supplied them.</param>
/// <param name="Handle">The entry's handle, if the server supplied it.</param>
public readonly record struct Nfs3DirEntryPlus(
    ulong FileId,
    string Name,
    ulong Cookie,
    Nfs3FileAttributes? Attributes,
    Nfs3Handle? Handle);

/// <summary>Arguments for READDIRPLUS (<c>READDIRPLUS3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadDirPlusArgs
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

    /// <summary>A hint for the maximum number of bytes of directory information to return.</summary>
    [XdrField(3)]
    public uint DirectoryCount { get; set; }

    /// <summary>A hint for the maximum number of bytes in the whole reply.</summary>
    [XdrField(4)]
    public uint MaxCount { get; set; }
}

/// <summary>The failure arm of READDIRPLUS (<c>READDIRPLUS3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadDirPlusResultFail
{
    /// <summary>The directory's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }
}

/// <summary>The success arm of READDIRPLUS (<c>READDIRPLUS3resok</c>, RFC 1813).</summary>
public record struct Nfs3ReadDirPlusResultOk : IXdrSerializable<Nfs3ReadDirPlusResultOk>
{
    /// <summary>The directory's attributes, if available.</summary>
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }

    /// <summary>The cookie verifier for this listing (8 bytes).</summary>
    public byte[] CookieVerifier { get; set; }

    /// <summary>The directory entries.</summary>
    public Nfs3DirEntryPlus[] Entries { get; set; }

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

        foreach (Nfs3DirEntryPlus entry in Entries ?? [])
        {
            writer.WriteBool(true);
            writer.WriteUInt64(entry.FileId);
            writer.WriteString(entry.Name);
            writer.WriteUInt64(entry.Cookie);

            if (entry.Attributes is { } attributes)
            {
                writer.WriteBool(true);
                attributes.WriteTo(ref writer);
            }
            else
            {
                writer.WriteBool(false);
            }

            if (entry.Handle is { } handle)
            {
                writer.WriteBool(true);
                handle.WriteTo(ref writer);
            }
            else
            {
                writer.WriteBool(false);
            }
        }

        writer.WriteBool(false);
        writer.WriteBool(Eof);
    }

    /// <inheritdoc/>
    public static Nfs3ReadDirPlusResultOk ReadFrom(ref XdrReader reader)
    {
        Nfs3FileAttributes? attributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
        byte[] cookieVerifier = reader.ReadOpaqueFixed(8).ToArray();

        var entries = new List<Nfs3DirEntryPlus>();
        while (reader.ReadBool())
        {
            ulong fileId = reader.ReadUInt64();
            string name = reader.ReadString(Nfs3.MaxNameLength);
            ulong cookie = reader.ReadUInt64();
            Nfs3FileAttributes? entryAttributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
            Nfs3Handle? entryHandle = reader.ReadBool() ? Nfs3Handle.ReadFrom(ref reader) : null;
            entries.Add(new Nfs3DirEntryPlus(fileId, name, cookie, entryAttributes, entryHandle));
        }

        bool eof = reader.ReadBool();
        return new Nfs3ReadDirPlusResultOk
        {
            DirectoryAttributes = attributes,
            CookieVerifier = cookieVerifier,
            Entries = [.. entries],
            Eof = eof,
        };
    }
}

/// <summary>The result of READDIRPLUS (<c>READDIRPLUS3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3ReadDirPlusResult : IXdrSerializable<Nfs3ReadDirPlusResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3ReadDirPlusResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3ReadDirPlusResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadDirPlusResult Success(Nfs3ReadDirPlusResultOk ok) =>
        new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadDirPlusResult Failure(NfsStatus status, Nfs3ReadDirPlusResultFail fail = default) =>
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
    public static Nfs3ReadDirPlusResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3ReadDirPlusResult { Status = status, Ok = Nfs3ReadDirPlusResultOk.ReadFrom(ref reader) }
            : new Nfs3ReadDirPlusResult { Status = status, Fail = Nfs3ReadDirPlusResultFail.ReadFrom(ref reader) };
    }
}
