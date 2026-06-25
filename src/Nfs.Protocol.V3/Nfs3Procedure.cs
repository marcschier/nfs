namespace Nfs.Protocol.V3;

/// <summary>Identifies the NFS version 3 RPC program (RFC 1813).</summary>
public static class Nfs3
{
    /// <summary>The NFS RPC program number.</summary>
    public const uint Program = 100003;

    /// <summary>The NFS version 3 protocol version number.</summary>
    public const uint ProtocolVersion = 3;

    /// <summary>The largest READ payload, in bytes, this implementation handles.</summary>
    public const int MaxReadSize = 1024 * 1024;

    /// <summary>The largest WRITE payload, in bytes, this implementation handles.</summary>
    public const int MaxWriteSize = 1024 * 1024;

    /// <summary>The largest symbolic-link or path string, in bytes, this implementation handles.</summary>
    public const int MaxPathLength = 4096;

    /// <summary>The largest directory-entry name length, in bytes (<c>NFS3_MAXNAMLEN</c>).</summary>
    public const int MaxNameLength = 255;
}

/// <summary>How a server is asked to commit written data to stable storage (<c>stable_how</c>).</summary>
public enum Nfs3StableHow
{
    /// <summary>The server need not commit the data before replying (UNSTABLE).</summary>
    Unstable = 0,

    /// <summary>The server must commit the data, but not the metadata (DATA_SYNC).</summary>
    DataSync = 1,

    /// <summary>The server must commit the data and metadata (FILE_SYNC).</summary>
    FileSync = 2,
}

/// <summary>The NFS version 3 procedure numbers (RFC 1813).</summary>
public enum Nfs3Procedure
{
    /// <summary>Do nothing (NFSPROC3_NULL).</summary>
    Null = 0,

    /// <summary>Get file attributes (NFSPROC3_GETATTR).</summary>
    GetAttributes = 1,

    /// <summary>Set file attributes (NFSPROC3_SETATTR).</summary>
    SetAttributes = 2,

    /// <summary>Look up a name in a directory (NFSPROC3_LOOKUP).</summary>
    Lookup = 3,

    /// <summary>Check access permission (NFSPROC3_ACCESS).</summary>
    Access = 4,

    /// <summary>Read from a symbolic link (NFSPROC3_READLINK).</summary>
    ReadLink = 5,

    /// <summary>Read from a file (NFSPROC3_READ).</summary>
    Read = 6,

    /// <summary>Write to a file (NFSPROC3_WRITE).</summary>
    Write = 7,

    /// <summary>Create a file (NFSPROC3_CREATE).</summary>
    Create = 8,

    /// <summary>Create a directory (NFSPROC3_MKDIR).</summary>
    MakeDirectory = 9,

    /// <summary>Create a symbolic link (NFSPROC3_SYMLINK).</summary>
    SymbolicLink = 10,

    /// <summary>Create a special device (NFSPROC3_MKNOD).</summary>
    MakeNode = 11,

    /// <summary>Remove a file (NFSPROC3_REMOVE).</summary>
    Remove = 12,

    /// <summary>Remove a directory (NFSPROC3_RMDIR).</summary>
    RemoveDirectory = 13,

    /// <summary>Rename a file or directory (NFSPROC3_RENAME).</summary>
    Rename = 14,

    /// <summary>Create a hard link (NFSPROC3_LINK).</summary>
    Link = 15,

    /// <summary>Read from a directory (NFSPROC3_READDIR).</summary>
    ReadDirectory = 16,

    /// <summary>Read from a directory with attributes (NFSPROC3_READDIRPLUS).</summary>
    ReadDirectoryPlus = 17,

    /// <summary>Get dynamic file-system information (NFSPROC3_FSSTAT).</summary>
    FileSystemStatus = 18,

    /// <summary>Get static file-system information (NFSPROC3_FSINFO).</summary>
    FileSystemInfo = 19,

    /// <summary>Retrieve POSIX path information (NFSPROC3_PATHCONF).</summary>
    PathConfiguration = 20,

    /// <summary>Commit cached data to stable storage (NFSPROC3_COMMIT).</summary>
    Commit = 21,
}
