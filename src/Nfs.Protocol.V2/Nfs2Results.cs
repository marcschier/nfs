using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V2;

/// <summary>
/// A result that returns object attributes on success and nothing on failure
/// (<c>attrstat</c>, RFC 1094). Used by GETATTR, SETATTR, and WRITE.
/// </summary>
public record struct Nfs2AttrStat : IXdrSerializable<Nfs2AttrStat>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The object's attributes (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs2FileAttributes Attributes { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="attributes">The object's attributes.</param>
    /// <returns>The result.</returns>
    public static Nfs2AttrStat Success(Nfs2FileAttributes attributes) =>
        new() { Status = NfsStatus.Ok, Attributes = attributes };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2AttrStat Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Attributes.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs2AttrStat ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs2AttrStat { Status = status, Attributes = Nfs2FileAttributes.ReadFrom(ref reader) }
            : new Nfs2AttrStat { Status = status };
    }
}

/// <summary>
/// A result returning a handle and attributes on success (<c>diropres</c>, RFC 1094). Used by
/// LOOKUP, CREATE, MKDIR, and SYMLINK's directory operations.
/// </summary>
public record struct Nfs2DirOpResult : IXdrSerializable<Nfs2DirOpResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The resulting object's handle (valid on success).</summary>
    public Nfs2Handle Handle { get; set; }

    /// <summary>The resulting object's attributes (valid on success).</summary>
    public Nfs2FileAttributes Attributes { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="handle">The resulting object's handle.</param>
    /// <param name="attributes">The resulting object's attributes.</param>
    /// <returns>The result.</returns>
    public static Nfs2DirOpResult Success(Nfs2Handle handle, Nfs2FileAttributes attributes) =>
        new() { Status = NfsStatus.Ok, Handle = handle, Attributes = attributes };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2DirOpResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Handle.WriteTo(ref writer);
            Attributes.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs2DirOpResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        if (status != NfsStatus.Ok)
        {
            return new Nfs2DirOpResult { Status = status };
        }

        return new Nfs2DirOpResult
        {
            Status = status,
            Handle = Nfs2Handle.ReadFrom(ref reader),
            Attributes = Nfs2FileAttributes.ReadFrom(ref reader),
        };
    }
}

/// <summary>The result of READ (<c>readres</c>, RFC 1094).</summary>
public record struct Nfs2ReadResult : IXdrSerializable<Nfs2ReadResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The file's attributes after the read (valid on success).</summary>
    public Nfs2FileAttributes Attributes { get; set; }

    /// <summary>The bytes read (valid on success).</summary>
    public byte[]? Data { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="attributes">The file's attributes.</param>
    /// <param name="data">The bytes read.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadResult Success(Nfs2FileAttributes attributes, byte[] data) =>
        new() { Status = NfsStatus.Ok, Attributes = attributes, Data = data };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Attributes.WriteTo(ref writer);
            writer.WriteOpaqueVariable(Data ?? []);
        }
    }

    /// <inheritdoc/>
    public static Nfs2ReadResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        if (status != NfsStatus.Ok)
        {
            return new Nfs2ReadResult { Status = status };
        }

        Nfs2FileAttributes attributes = Nfs2FileAttributes.ReadFrom(ref reader);
        byte[] data = reader.ReadOpaqueVariable(Nfs2.MaxData).ToArray();
        return new Nfs2ReadResult { Status = status, Attributes = attributes, Data = data };
    }
}

/// <summary>The result of READLINK (<c>readlinkres</c>, RFC 1094).</summary>
public record struct Nfs2ReadLinkResult : IXdrSerializable<Nfs2ReadLinkResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The link target path (valid on success).</summary>
    public string? Target { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="target">The link target path.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadLinkResult Success(string target) => new() { Status = NfsStatus.Ok, Target = target };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadLinkResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            writer.WriteString(Target ?? string.Empty);
        }
    }

    /// <inheritdoc/>
    public static Nfs2ReadLinkResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs2ReadLinkResult { Status = status, Target = reader.ReadString(Nfs2.MaxPathLength) }
            : new Nfs2ReadLinkResult { Status = status };
    }
}

/// <summary>The result of STATFS (<c>statfsres</c>, RFC 1094).</summary>
public record struct Nfs2StatFsResult : IXdrSerializable<Nfs2StatFsResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The optimal transfer size, in bytes.</summary>
    public uint TransferSize { get; set; }

    /// <summary>The block size, in bytes.</summary>
    public uint BlockSize { get; set; }

    /// <summary>The total number of blocks.</summary>
    public uint TotalBlocks { get; set; }

    /// <summary>The number of free blocks.</summary>
    public uint FreeBlocks { get; set; }

    /// <summary>The number of blocks available to an unprivileged user.</summary>
    public uint AvailableBlocks { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2StatFsResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            writer.WriteUInt32(TransferSize);
            writer.WriteUInt32(BlockSize);
            writer.WriteUInt32(TotalBlocks);
            writer.WriteUInt32(FreeBlocks);
            writer.WriteUInt32(AvailableBlocks);
        }
    }

    /// <inheritdoc/>
    public static Nfs2StatFsResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        if (status != NfsStatus.Ok)
        {
            return new Nfs2StatFsResult { Status = status };
        }

        return new Nfs2StatFsResult
        {
            Status = status,
            TransferSize = reader.ReadUInt32(),
            BlockSize = reader.ReadUInt32(),
            TotalBlocks = reader.ReadUInt32(),
            FreeBlocks = reader.ReadUInt32(),
            AvailableBlocks = reader.ReadUInt32(),
        };
    }
}

/// <summary>A single READDIR entry (<c>entry</c>, RFC 1094).</summary>
/// <param name="FileId">The object's unique identifier within the file system.</param>
/// <param name="Name">The entry name.</param>
/// <param name="Cookie">The opaque continuation cookie positioned after this entry (4 bytes).</param>
public readonly record struct Nfs2DirEntry(uint FileId, string Name, byte[] Cookie);

/// <summary>The result of READDIR (<c>readdirres</c>, RFC 1094).</summary>
public record struct Nfs2ReadDirResult : IXdrSerializable<Nfs2ReadDirResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The directory entries (valid on success).</summary>
    public Nfs2DirEntry[] Entries { get; set; }

    /// <summary>Whether the end of the directory was reached.</summary>
    public bool Eof { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="entries">The directory entries.</param>
    /// <param name="eof">Whether the end of the directory was reached.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadDirResult Success(Nfs2DirEntry[] entries, bool eof) =>
        new() { Status = NfsStatus.Ok, Entries = entries, Eof = eof };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs2ReadDirResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status != NfsStatus.Ok)
        {
            return;
        }

        foreach (Nfs2DirEntry entry in Entries ?? [])
        {
            writer.WriteBool(true);
            writer.WriteUInt32(entry.FileId);
            writer.WriteString(entry.Name);
            writer.WriteOpaqueFixed(entry.Cookie ?? new byte[4]);
        }

        writer.WriteBool(false);
        writer.WriteBool(Eof);
    }

    /// <inheritdoc/>
    public static Nfs2ReadDirResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        if (status != NfsStatus.Ok)
        {
            return new Nfs2ReadDirResult { Status = status };
        }

        var entries = new List<Nfs2DirEntry>();
        while (reader.ReadBool())
        {
            uint fileId = reader.ReadUInt32();
            string name = reader.ReadString(Nfs2.MaxNameLength);
            byte[] cookie = reader.ReadOpaqueFixed(4).ToArray();
            entries.Add(new Nfs2DirEntry(fileId, name, cookie));
        }

        bool eof = reader.ReadBool();
        return new Nfs2ReadDirResult { Status = status, Entries = [.. entries], Eof = eof };
    }
}

/// <summary>A result carrying only a status (<c>nfsstat</c>, RFC 1094). Used by REMOVE, RENAME, etc.</summary>
public record struct Nfs2StatResult : IXdrSerializable<Nfs2StatResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a result with the given status.</summary>
    /// <param name="status">The status.</param>
    /// <returns>The result.</returns>
    public static Nfs2StatResult Create(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer) => writer.WriteInt32((int)Status);

    /// <inheritdoc/>
    public static Nfs2StatResult ReadFrom(ref XdrReader reader) => new() { Status = (NfsStatus)reader.ReadInt32() };
}
