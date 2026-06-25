namespace Nfs.Abstractions;

/// <summary>
/// The attributes of a file-system object, normalized across NFS versions (modeled on
/// <c>fattr3</c>, RFC 1813). Backends produce these; the protocol layers map them to the wire.
/// </summary>
public sealed record NfsFileAttributes
{
    /// <summary>Gets the object type.</summary>
    public NfsFileType Type { get; init; }

    /// <summary>Gets the protection mode bits.</summary>
    public uint Mode { get; init; }

    /// <summary>Gets the number of hard links.</summary>
    public uint LinkCount { get; init; } = 1;

    /// <summary>Gets the owner's user id.</summary>
    public uint Uid { get; init; }

    /// <summary>Gets the owner's group id.</summary>
    public uint Gid { get; init; }

    /// <summary>Gets the size of the object in bytes.</summary>
    public ulong Size { get; init; }

    /// <summary>Gets the bytes of storage the object consumes.</summary>
    public ulong Used { get; init; }

    /// <summary>Gets the object's unique identifier within the file system.</summary>
    public ulong FileId { get; init; }

    /// <summary>Gets the last access time.</summary>
    public NfsTimestamp AccessTime { get; init; }

    /// <summary>Gets the last data-modification time.</summary>
    public NfsTimestamp ModifyTime { get; init; }

    /// <summary>Gets the last attribute-change time.</summary>
    public NfsTimestamp ChangeTime { get; init; }
}
