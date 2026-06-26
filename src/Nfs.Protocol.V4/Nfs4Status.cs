using Nfs.Abstractions;

namespace Nfs.Protocol.V4;

/// <summary>NFS version 4 status codes (<c>nfsstat4</c>, RFC 7530).</summary>
public enum Nfs4Status
{
    /// <summary>The operation completed successfully (NFS4_OK).</summary>
    Ok = 0,

    /// <summary>The caller is not the owner (NFS4ERR_PERM).</summary>
    NotOwner = 1,

    /// <summary>No such file or directory (NFS4ERR_NOENT).</summary>
    NoEntry = 2,

    /// <summary>A hard I/O error occurred (NFS4ERR_IO).</summary>
    IoError = 5,

    /// <summary>No such device or address (NFS4ERR_NXIO).</summary>
    NoSuchDeviceOrAddress = 6,

    /// <summary>Permission denied (NFS4ERR_ACCESS).</summary>
    AccessDenied = 13,

    /// <summary>The file already exists (NFS4ERR_EXIST).</summary>
    AlreadyExists = 17,

    /// <summary>An attempt was made to cross a device boundary (NFS4ERR_XDEV).</summary>
    CrossDeviceLink = 18,

    /// <summary>The target is not a directory (NFS4ERR_NOTDIR).</summary>
    NotDirectory = 20,

    /// <summary>The target is a directory (NFS4ERR_ISDIR).</summary>
    IsDirectory = 21,

    /// <summary>An argument was invalid (NFS4ERR_INVAL).</summary>
    InvalidArgument = 22,

    /// <summary>The file is too large (NFS4ERR_FBIG).</summary>
    FileTooLarge = 27,

    /// <summary>No space left on the device (NFS4ERR_NOSPC).</summary>
    NoSpace = 28,

    /// <summary>The file system is read-only (NFS4ERR_ROFS).</summary>
    ReadOnlyFileSystem = 30,

    /// <summary>Too many hard links (NFS4ERR_MLINK).</summary>
    TooManyLinks = 31,

    /// <summary>A name is too long (NFS4ERR_NAMETOOLONG).</summary>
    NameTooLong = 63,

    /// <summary>The directory is not empty (NFS4ERR_NOTEMPTY).</summary>
    DirectoryNotEmpty = 66,

    /// <summary>The user's disk quota has been exceeded (NFS4ERR_DQUOT).</summary>
    QuotaExceeded = 69,

    /// <summary>The file handle refers to an object that no longer exists (NFS4ERR_STALE).</summary>
    StaleHandle = 70,

    /// <summary>The file handle is structurally invalid (NFS4ERR_BADHANDLE).</summary>
    BadHandle = 10001,

    /// <summary>A cookie is no longer valid (NFS4ERR_BAD_COOKIE).</summary>
    BadCookie = 10003,

    /// <summary>The operation is not supported (NFS4ERR_NOTSUPP).</summary>
    NotSupported = 10004,

    /// <summary>A response buffer was too small (NFS4ERR_TOOSMALL).</summary>
    TooSmall = 10005,

    /// <summary>The server encountered an internal fault (NFS4ERR_SERVERFAULT).</summary>
    ServerFault = 10006,

    /// <summary>The object type is wrong for the operation (NFS4ERR_BADTYPE).</summary>
    BadType = 10007,

    /// <summary>The request was initiated but not completed; retry later (NFS4ERR_DELAY).</summary>
    Delay = 10008,

    /// <summary>The compared attributes were the same (NFS4ERR_SAME).</summary>
    Same = 10009,

    /// <summary>A byte-range lock conflicts with an existing lock (NFS4ERR_DENIED).</summary>
    LockDenied = 10010,

    /// <summary>The server is in reboot grace and only reclaim operations are allowed (NFS4ERR_GRACE).</summary>
    Grace = 10013,

    /// <summary>The server is not in a grace period, so reclaim is not allowed (NFS4ERR_NO_GRACE).</summary>
    NoGrace = 10033,

    /// <summary>The client is not permitted to reclaim the requested state (NFS4ERR_RECLAIM_BAD).</summary>
    ReclaimBad = 10034,

    /// <summary>A lock-owner has outstanding state preventing the operation (NFS4ERR_LOCKS_HELD).</summary>
    LocksHeld = 10037,

    /// <summary>The state identifier is invalid (NFS4ERR_BAD_STATEID).</summary>
    BadStateId = 10025,

    /// <summary>The session identifier is invalid (NFS4ERR_BADSESSION).</summary>
    BadSession = 10052,

    /// <summary>The slot identifier is out of range (NFS4ERR_BADSLOT).</summary>
    BadSlot = 10053,

    /// <summary>No pNFS layout is currently available (NFS4ERR_LAYOUTUNAVAILABLE).</summary>
    LayoutUnavailable = 10059,

    /// <summary>The sequence value is out of order for the slot (NFS4ERR_SEQ_MISORDERED).</summary>
    SequenceMisordered = 10063,

    /// <summary>No current file handle is set (NFS4ERR_NOFILEHANDLE).</summary>
    NoFileHandle = 10020,

    /// <summary>The client identifier is stale or unknown (NFS4ERR_STALE_CLIENTID).</summary>
    StaleClientId = 10022,

    /// <summary>The minor version is not supported (NFS4ERR_MINOR_VERS_MISMATCH).</summary>
    MinorVersionMismatch = 10021,

    /// <summary>The compared attributes were not the same (NFS4ERR_NOT_SAME).</summary>
    NotSame = 10027,

    /// <summary>An operation was used that is not valid for the current file handle type.</summary>
    WrongType = 10083,

    /// <summary>An operation code is not supported in this context (NFS4ERR_OP_ILLEGAL).</summary>
    OperationIllegal = 10044,

    /// <summary>The extended attribute does not exist (NFS4ERR_NOXATTR).</summary>
    NoExtendedAttribute = 10095,

    /// <summary>The extended attribute value or set is too large (NFS4ERR_XATTR2BIG).</summary>
    ExtendedAttributeTooBig = 10096,
}

/// <summary>Maps the version-independent <see cref="NfsStatus"/> onto <see cref="Nfs4Status"/>.</summary>
public static class Nfs4StatusMapping
{
    /// <summary>Converts an <see cref="NfsStatus"/> to the closest <see cref="Nfs4Status"/>.</summary>
    /// <param name="status">The version-independent status.</param>
    /// <returns>The version 4 status.</returns>
    public static Nfs4Status FromStatus(NfsStatus status) => status switch
    {
        NfsStatus.Ok => Nfs4Status.Ok,
        NfsStatus.NotOwner => Nfs4Status.NotOwner,
        NfsStatus.NoEntry => Nfs4Status.NoEntry,
        NfsStatus.IoError => Nfs4Status.IoError,
        NfsStatus.NoSuchDeviceOrAddress => Nfs4Status.NoSuchDeviceOrAddress,
        NfsStatus.AccessDenied => Nfs4Status.AccessDenied,
        NfsStatus.AlreadyExists => Nfs4Status.AlreadyExists,
        NfsStatus.CrossDeviceLink => Nfs4Status.CrossDeviceLink,
        NfsStatus.NotDirectory => Nfs4Status.NotDirectory,
        NfsStatus.IsDirectory => Nfs4Status.IsDirectory,
        NfsStatus.InvalidArgument => Nfs4Status.InvalidArgument,
        NfsStatus.FileTooLarge => Nfs4Status.FileTooLarge,
        NfsStatus.NoSpace => Nfs4Status.NoSpace,
        NfsStatus.ReadOnlyFileSystem => Nfs4Status.ReadOnlyFileSystem,
        NfsStatus.TooManyLinks => Nfs4Status.TooManyLinks,
        NfsStatus.NameTooLong => Nfs4Status.NameTooLong,
        NfsStatus.DirectoryNotEmpty => Nfs4Status.DirectoryNotEmpty,
        NfsStatus.QuotaExceeded => Nfs4Status.QuotaExceeded,
        NfsStatus.StaleHandle => Nfs4Status.StaleHandle,
        NfsStatus.BadHandle => Nfs4Status.BadHandle,
        NfsStatus.BadCookie => Nfs4Status.BadCookie,
        NfsStatus.NotSupported => Nfs4Status.NotSupported,
        NfsStatus.TooSmall => Nfs4Status.TooSmall,
        NfsStatus.BadType => Nfs4Status.BadType,
        NfsStatus.Jukebox => Nfs4Status.Delay,
        NfsStatus.NoExtendedAttribute => Nfs4Status.NoExtendedAttribute,
        NfsStatus.ExtendedAttributeTooBig => Nfs4Status.ExtendedAttributeTooBig,
        _ => Nfs4Status.ServerFault,
    };
}
