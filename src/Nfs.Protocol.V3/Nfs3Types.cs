using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Device numbers for a special file (<c>specdata3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3SpecData
{
    /// <summary>The major device number.</summary>
    [XdrField(0)]
    public uint Major { get; set; }

    /// <summary>The minor device number.</summary>
    [XdrField(1)]
    public uint Minor { get; set; }
}

/// <summary>A timestamp with nanosecond resolution (<c>nfstime3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3Time
{
    /// <summary>Whole seconds since the Unix epoch.</summary>
    [XdrField(0)]
    public uint Seconds { get; set; }

    /// <summary>Nanoseconds within the second.</summary>
    [XdrField(1)]
    public uint Nanoseconds { get; set; }
}

/// <summary>An NFS version 3 file handle (<c>nfs_fh3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3Handle
{
    /// <summary>The opaque handle bytes (at most 64).</summary>
    [XdrField(0)]
    [XdrOpaque(64)]
    public byte[] Data { get; set; }
}

/// <summary>The attributes of a file-system object (<c>fattr3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3FileAttributes
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
    public ulong Size { get; set; }

    /// <summary>The bytes of disk space the object consumes.</summary>
    [XdrField(6)]
    public ulong Used { get; set; }

    /// <summary>The device number, for special files.</summary>
    [XdrField(7)]
    public Nfs3SpecData Rdev { get; set; }

    /// <summary>The file-system identifier.</summary>
    [XdrField(8)]
    public ulong FileSystemId { get; set; }

    /// <summary>The object's unique identifier within the file system.</summary>
    [XdrField(9)]
    public ulong FileId { get; set; }

    /// <summary>The last access time.</summary>
    [XdrField(10)]
    public Nfs3Time AccessTime { get; set; }

    /// <summary>The last data modification time.</summary>
    [XdrField(11)]
    public Nfs3Time ModifyTime { get; set; }

    /// <summary>The last attribute-change time.</summary>
    [XdrField(12)]
    public Nfs3Time ChangeTime { get; set; }
}

/// <summary>
/// Weak cache-consistency attributes captured before an operation (<c>wcc_attr</c>, RFC 1813).
/// </summary>
[XdrType]
public partial struct Nfs3WccAttributes
{
    /// <summary>The file size before the operation.</summary>
    [XdrField(0)]
    public ulong Size { get; set; }

    /// <summary>The modification time before the operation.</summary>
    [XdrField(1)]
    public Nfs3Time ModifyTime { get; set; }

    /// <summary>The attribute-change time before the operation.</summary>
    [XdrField(2)]
    public Nfs3Time ChangeTime { get; set; }
}

/// <summary>Weak cache-consistency data bracketing an operation (<c>wcc_data</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3WccData
{
    /// <summary>The pre-operation attributes, when the server supplied them (<c>pre_op_attr</c>).</summary>
    [XdrField(0)]
    public Nfs3WccAttributes? Before { get; set; }

    /// <summary>The post-operation attributes, when the server supplied them (<c>post_op_attr</c>).</summary>
    [XdrField(1)]
    public Nfs3FileAttributes? After { get; set; }
}

/// <summary>Arguments naming an object within a directory (<c>diropargs3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3DirOpArgs
{
    /// <summary>The directory handle.</summary>
    [XdrField(0)]
    public Nfs3Handle Directory { get; set; }

    /// <summary>The object name within the directory.</summary>
    [XdrField(1)]
    [XdrString(255)]
    public string Name { get; set; }
}
