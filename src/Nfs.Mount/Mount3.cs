namespace Nfs.Mount;

/// <summary>Identifies the MOUNT version 3 RPC program (RFC 1813 appendix I).</summary>
public static class Mount3
{
    /// <summary>The MOUNT RPC program number.</summary>
    public const uint Program = 100005;

    /// <summary>The MOUNT version 3 protocol version number.</summary>
    public const uint ProtocolVersion = 3;

    /// <summary>The maximum length of a mount path, in bytes (MNTPATHLEN).</summary>
    public const int MaxPathLength = 1024;

    /// <summary>The maximum length of host and group names used by MOUNT lists.</summary>
    public const int MaxNameLength = 255;
}

/// <summary>The MOUNT version 3 procedure numbers (RFC 1813 appendix I).</summary>
public enum Mount3Procedure
{
    /// <summary>Do nothing (MOUNTPROC3_NULL).</summary>
    Null = 0,

    /// <summary>Add a mount entry and return the export's root handle (MOUNTPROC3_MNT).</summary>
    Mount = 1,

    /// <summary>Return the server's mount entries (MOUNTPROC3_DUMP).</summary>
    Dump = 2,

    /// <summary>Remove a mount entry (MOUNTPROC3_UMNT).</summary>
    Unmount = 3,

    /// <summary>Remove all of a client's mount entries (MOUNTPROC3_UMNTALL).</summary>
    UnmountAll = 4,

    /// <summary>Return the server's export list (MOUNTPROC3_EXPORT).</summary>
    Export = 5,
}

/// <summary>The status of a MOUNT operation (<c>mountstat3</c>, RFC 1813).</summary>
public enum Mount3Status
{
    /// <summary>The call succeeded (MNT3_OK).</summary>
    Ok = 0,

    /// <summary>Not owner (MNT3ERR_PERM).</summary>
    NotOwner = 1,

    /// <summary>No such file or directory (MNT3ERR_NOENT).</summary>
    NoEntry = 2,

    /// <summary>An I/O error occurred (MNT3ERR_IO).</summary>
    IoError = 5,

    /// <summary>Permission denied (MNT3ERR_ACCES).</summary>
    AccessDenied = 13,

    /// <summary>Not a directory (MNT3ERR_NOTDIR).</summary>
    NotDirectory = 20,

    /// <summary>An argument was invalid (MNT3ERR_INVAL).</summary>
    InvalidArgument = 22,

    /// <summary>The path is too long (MNT3ERR_NAMETOOLONG).</summary>
    NameTooLong = 63,

    /// <summary>The operation is not supported (MNT3ERR_NOTSUPP).</summary>
    NotSupported = 10004,

    /// <summary>The server faulted (MNT3ERR_SERVERFAULT).</summary>
    ServerFault = 10006,
}
