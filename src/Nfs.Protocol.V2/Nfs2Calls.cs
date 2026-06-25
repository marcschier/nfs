using Nfs.Xdr;

namespace Nfs.Protocol.V2;

/// <summary>Arguments for GETATTR, READLINK, and STATFS (a bare handle, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2HandleArgs
{
    /// <summary>The object handle.</summary>
    [XdrField(0)]
    public Nfs2Handle Handle { get; set; }
}

/// <summary>Arguments for SETATTR (<c>sattrargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2SetAttrArgs
{
    /// <summary>The object to modify.</summary>
    [XdrField(0)]
    public Nfs2Handle Handle { get; set; }

    /// <summary>The attributes to set.</summary>
    [XdrField(1)]
    public Nfs2SetAttributes Attributes { get; set; }
}

/// <summary>Arguments for READ (<c>readargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2ReadArgs
{
    /// <summary>The file handle.</summary>
    [XdrField(0)]
    public Nfs2Handle File { get; set; }

    /// <summary>The byte offset to read from.</summary>
    [XdrField(1)]
    public uint Offset { get; set; }

    /// <summary>The number of bytes to read.</summary>
    [XdrField(2)]
    public uint Count { get; set; }

    /// <summary>An obsolete total-count hint, retained for wire compatibility.</summary>
    [XdrField(3)]
    public uint TotalCount { get; set; }
}

/// <summary>Arguments for WRITE (<c>writeargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2WriteArgs
{
    /// <summary>The file handle.</summary>
    [XdrField(0)]
    public Nfs2Handle File { get; set; }

    /// <summary>An obsolete begin-offset hint, retained for wire compatibility.</summary>
    [XdrField(1)]
    public uint BeginOffset { get; set; }

    /// <summary>The byte offset to write at.</summary>
    [XdrField(2)]
    public uint Offset { get; set; }

    /// <summary>An obsolete total-count hint, retained for wire compatibility.</summary>
    [XdrField(3)]
    public uint TotalCount { get; set; }

    /// <summary>The data to write.</summary>
    [XdrField(4)]
    [XdrOpaque(Nfs2.MaxData)]
    public byte[] Data { get; set; }
}

/// <summary>Arguments for CREATE and MKDIR (<c>createargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2CreateArgs
{
    /// <summary>The parent directory and new name.</summary>
    [XdrField(0)]
    public Nfs2DirOpArgs Where { get; set; }

    /// <summary>The initial attributes.</summary>
    [XdrField(1)]
    public Nfs2SetAttributes Attributes { get; set; }
}

/// <summary>Arguments for RENAME (<c>renameargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2RenameArgs
{
    /// <summary>The source directory and name.</summary>
    [XdrField(0)]
    public Nfs2DirOpArgs From { get; set; }

    /// <summary>The destination directory and name.</summary>
    [XdrField(1)]
    public Nfs2DirOpArgs To { get; set; }
}

/// <summary>Arguments for LINK (<c>linkargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2LinkArgs
{
    /// <summary>The existing object to link to.</summary>
    [XdrField(0)]
    public Nfs2Handle From { get; set; }

    /// <summary>The directory and name of the new link.</summary>
    [XdrField(1)]
    public Nfs2DirOpArgs To { get; set; }
}

/// <summary>Arguments for SYMLINK (<c>symlinkargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2SymlinkArgs
{
    /// <summary>The directory and name of the new link.</summary>
    [XdrField(0)]
    public Nfs2DirOpArgs From { get; set; }

    /// <summary>The path the link points to.</summary>
    [XdrField(1)]
    [XdrString(Nfs2.MaxPathLength)]
    public string Target { get; set; }

    /// <summary>The initial attributes.</summary>
    [XdrField(2)]
    public Nfs2SetAttributes Attributes { get; set; }
}

/// <summary>Arguments for READDIR (<c>readdirargs</c>, RFC 1094).</summary>
[XdrType]
public partial struct Nfs2ReadDirArgs
{
    /// <summary>The directory handle.</summary>
    [XdrField(0)]
    public Nfs2Handle Directory { get; set; }

    /// <summary>The opaque continuation cookie (4 bytes; all zero to start).</summary>
    [XdrField(1)]
    [XdrFixedOpaque(4)]
    public byte[] Cookie { get; set; }

    /// <summary>A hint for the maximum number of bytes of entries to return.</summary>
    [XdrField(2)]
    public uint Count { get; set; }
}
