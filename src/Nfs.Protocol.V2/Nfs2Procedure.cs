namespace Nfs.Protocol.V2;

/// <summary>Identifies the NFS version 2 RPC program (RFC 1094).</summary>
public static class Nfs2
{
    /// <summary>The NFS RPC program number (shared with version 3).</summary>
    public const uint Program = 100003;

    /// <summary>The NFS version 2 protocol version number.</summary>
    public const uint ProtocolVersion = 2;

    /// <summary>The fixed size, in bytes, of a version 2 file handle (<c>FHSIZE</c>).</summary>
    public const int HandleSize = 32;

    /// <summary>The largest READ or WRITE payload, in bytes (<c>NFS_MAXDATA</c>).</summary>
    public const int MaxData = 8192;

    /// <summary>The largest file-name length, in bytes (<c>NFS_MAXNAMLEN</c>).</summary>
    public const int MaxNameLength = 255;

    /// <summary>The largest path length, in bytes (<c>NFS_MAXPATHLEN</c>).</summary>
    public const int MaxPathLength = 1024;

    /// <summary>The sentinel that means "do not change this field" in <c>sattr</c>.</summary>
    public const uint Unchanged = 0xFFFFFFFFu;
}

/// <summary>The NFS version 2 procedure numbers (RFC 1094).</summary>
public enum Nfs2Procedure
{
    /// <summary>Do nothing (NFSPROC_NULL).</summary>
    Null = 0,

    /// <summary>Get file attributes (NFSPROC_GETATTR).</summary>
    GetAttributes = 1,

    /// <summary>Set file attributes (NFSPROC_SETATTR).</summary>
    SetAttributes = 2,

    /// <summary>Obsolete root lookup (NFSPROC_ROOT); unused.</summary>
    Root = 3,

    /// <summary>Look up a name in a directory (NFSPROC_LOOKUP).</summary>
    Lookup = 4,

    /// <summary>Read from a symbolic link (NFSPROC_READLINK).</summary>
    ReadLink = 5,

    /// <summary>Read from a file (NFSPROC_READ).</summary>
    Read = 6,

    /// <summary>Obsolete write-cache (NFSPROC_WRITECACHE); unused.</summary>
    WriteCache = 7,

    /// <summary>Write to a file (NFSPROC_WRITE).</summary>
    Write = 8,

    /// <summary>Create a file (NFSPROC_CREATE).</summary>
    Create = 9,

    /// <summary>Remove a file (NFSPROC_REMOVE).</summary>
    Remove = 10,

    /// <summary>Rename a file or directory (NFSPROC_RENAME).</summary>
    Rename = 11,

    /// <summary>Create a hard link (NFSPROC_LINK).</summary>
    Link = 12,

    /// <summary>Create a symbolic link (NFSPROC_SYMLINK).</summary>
    SymbolicLink = 13,

    /// <summary>Create a directory (NFSPROC_MKDIR).</summary>
    MakeDirectory = 14,

    /// <summary>Remove a directory (NFSPROC_RMDIR).</summary>
    RemoveDirectory = 15,

    /// <summary>Read from a directory (NFSPROC_READDIR).</summary>
    ReadDirectory = 16,

    /// <summary>Get file-system attributes (NFSPROC_STATFS).</summary>
    FileSystemStatus = 17,
}
