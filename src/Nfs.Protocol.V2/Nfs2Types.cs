using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V2;

/// <summary>A timestamp with microsecond resolution (<c>timeval</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2Time
{
    /// <summary>Whole seconds since the Unix epoch.</summary>
    [XdrField(0)]
    public uint Seconds { get; set; }

    /// <summary>Microseconds within the second.</summary>
    [XdrField(1)]
    public uint MicroSeconds { get; set; }
}

/// <summary>An NFS version 2 file handle (<c>fhandle</c>, RFC 1094): a fixed 32-byte opaque.</summary>
[XdrType]
public partial struct Nfs2Handle
{
    /// <summary>The 32 opaque handle bytes.</summary>
    [XdrField(0)]
    [XdrFixedOpaque(Nfs2.HandleSize)]
    public byte[] Data { get; set; }
}

/// <summary>The attributes of a file-system object (<c>fattr</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2FileAttributes
{
    /// <summary>The object type.</summary>
    [XdrField(0)]
    public NfsFileType Type { get; set; }

    /// <summary>The protection mode bits.</summary>
    [XdrField(1)]
    public uint Mode { get; set; }

    /// <summary>The number of hard links.</summary>
    [XdrField(2)]
    public uint LinkCount { get; set; }

    /// <summary>The owner's user id.</summary>
    [XdrField(3)]
    public uint Uid { get; set; }

    /// <summary>The owner's group id.</summary>
    [XdrField(4)]
    public uint Gid { get; set; }

    /// <summary>The file size in bytes.</summary>
    [XdrField(5)]
    public uint Size { get; set; }

    /// <summary>The preferred block size for I/O.</summary>
    [XdrField(6)]
    public uint BlockSize { get; set; }

    /// <summary>The device number, for special files.</summary>
    [XdrField(7)]
    public uint Rdev { get; set; }

    /// <summary>The number of disk blocks the object consumes.</summary>
    [XdrField(8)]
    public uint Blocks { get; set; }

    /// <summary>The file-system identifier.</summary>
    [XdrField(9)]
    public uint FileSystemId { get; set; }

    /// <summary>The object's unique identifier within the file system.</summary>
    [XdrField(10)]
    public uint FileId { get; set; }

    /// <summary>The last access time.</summary>
    [XdrField(11)]
    public Nfs2Time AccessTime { get; set; }

    /// <summary>The last data-modification time.</summary>
    [XdrField(12)]
    public Nfs2Time ModifyTime { get; set; }

    /// <summary>The last attribute-change time.</summary>
    [XdrField(13)]
    public Nfs2Time ChangeTime { get; set; }
}

/// <summary>
/// Settable file attributes (<c>sattr</c>, RFC 1094). Each numeric field uses
/// <see cref="Nfs2.Unchanged"/> (-1) to mean "leave unchanged"; a time with a seconds value of -1
/// is likewise left unchanged.
/// </summary>
[XdrType]
public partial struct Nfs2SetAttributes
{
    /// <summary>The new mode bits, or <see cref="Nfs2.Unchanged"/> to leave unchanged.</summary>
    [XdrField(0)]
    public uint Mode { get; set; }

    /// <summary>The new owner user id, or <see cref="Nfs2.Unchanged"/> to leave unchanged.</summary>
    [XdrField(1)]
    public uint Uid { get; set; }

    /// <summary>The new owner group id, or <see cref="Nfs2.Unchanged"/> to leave unchanged.</summary>
    [XdrField(2)]
    public uint Gid { get; set; }

    /// <summary>The new size, or <see cref="Nfs2.Unchanged"/> to leave unchanged.</summary>
    [XdrField(3)]
    public uint Size { get; set; }

    /// <summary>The new access time (seconds of -1 means unchanged).</summary>
    [XdrField(4)]
    public Nfs2Time AccessTime { get; set; }

    /// <summary>The new modification time (seconds of -1 means unchanged).</summary>
    [XdrField(5)]
    public Nfs2Time ModifyTime { get; set; }

    /// <summary>Gets a value that changes nothing.</summary>
    public static Nfs2SetAttributes None => new()
    {
        Mode = Nfs2.Unchanged,
        Uid = Nfs2.Unchanged,
        Gid = Nfs2.Unchanged,
        Size = Nfs2.Unchanged,
        AccessTime = new Nfs2Time { Seconds = Nfs2.Unchanged, MicroSeconds = Nfs2.Unchanged },
        ModifyTime = new Nfs2Time { Seconds = Nfs2.Unchanged, MicroSeconds = Nfs2.Unchanged },
    };
}

/// <summary>Arguments naming an object within a directory (<c>diropargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2DirOpArgs
{
    /// <summary>The directory handle.</summary>
    [XdrField(0)]
    public Nfs2Handle Directory { get; set; }

    /// <summary>The object name within the directory.</summary>
    [XdrField(1)]
    [XdrString(Nfs2.MaxNameLength)]
    public string Name { get; set; }
}
