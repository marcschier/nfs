namespace Nfs.Abstractions;

/// <summary>
/// NFS operation status codes. The values are the NFS version 3 status codes (<c>nfsstat3</c>,
/// RFC 1813); version 2 uses a compatible subset and version 4 maps onto these where they overlap.
/// </summary>
public enum NfsStatus
{
    /// <summary>The operation completed successfully (NFS3_OK).</summary>
    Ok = 0,

    /// <summary>The caller is not the owner of the target (NFS3ERR_PERM).</summary>
    NotOwner = 1,

    /// <summary>No such file or directory (NFS3ERR_NOENT).</summary>
    NoEntry = 2,

    /// <summary>A hard error occurred while reading or writing (NFS3ERR_IO).</summary>
    IoError = 5,

    /// <summary>No such device or address (NFS3ERR_NXIO).</summary>
    NoSuchDeviceOrAddress = 6,

    /// <summary>Permission denied (NFS3ERR_ACCES).</summary>
    AccessDenied = 13,

    /// <summary>The file already exists (NFS3ERR_EXIST).</summary>
    AlreadyExists = 17,

    /// <summary>An attempt was made to cross a device boundary (NFS3ERR_XDEV).</summary>
    CrossDeviceLink = 18,

    /// <summary>No such device (NFS3ERR_NODEV).</summary>
    NoSuchDevice = 19,

    /// <summary>The target is not a directory (NFS3ERR_NOTDIR).</summary>
    NotDirectory = 20,

    /// <summary>The target is a directory (NFS3ERR_ISDIR).</summary>
    IsDirectory = 21,

    /// <summary>An argument was invalid (NFS3ERR_INVAL).</summary>
    InvalidArgument = 22,

    /// <summary>The file is too large (NFS3ERR_FBIG).</summary>
    FileTooLarge = 27,

    /// <summary>No space left on the device (NFS3ERR_NOSPC).</summary>
    NoSpace = 28,

    /// <summary>The file system is read-only (NFS3ERR_ROFS).</summary>
    ReadOnlyFileSystem = 30,

    /// <summary>Too many hard links (NFS3ERR_MLINK).</summary>
    TooManyLinks = 31,

    /// <summary>A path component or name is too long (NFS3ERR_NAMETOOLONG).</summary>
    NameTooLong = 63,

    /// <summary>The directory is not empty (NFS3ERR_NOTEMPTY).</summary>
    DirectoryNotEmpty = 66,

    /// <summary>The user's disk quota has been exceeded (NFS3ERR_DQUOT).</summary>
    QuotaExceeded = 69,

    /// <summary>The file handle refers to an object that no longer exists (NFS3ERR_STALE).</summary>
    StaleHandle = 70,

    /// <summary>The object is on a remote file system not reachable by the server (NFS3ERR_REMOTE).</summary>
    Remote = 71,

    /// <summary>The file handle is structurally invalid (NFS3ERR_BADHANDLE).</summary>
    BadHandle = 10001,

    /// <summary>A server cache update could not be applied synchronously (NFS3ERR_NOT_SYNC).</summary>
    NotSynchronized = 10002,

    /// <summary>A READDIR or READDIRPLUS cookie is no longer valid (NFS3ERR_BAD_COOKIE).</summary>
    BadCookie = 10003,

    /// <summary>The operation is not supported (NFS3ERR_NOTSUPP).</summary>
    NotSupported = 10004,

    /// <summary>A buffer or request was too small (NFS3ERR_TOOSMALL).</summary>
    TooSmall = 10005,

    /// <summary>The server encountered an internal fault (NFS3ERR_SERVERFAULT).</summary>
    ServerFault = 10006,

    /// <summary>The object type is not supported for the operation (NFS3ERR_BADTYPE).</summary>
    BadType = 10007,

    /// <summary>The request was initiated but not completed; the caller should retry (NFS3ERR_JUKEBOX).</summary>
    Jukebox = 10008,

    /// <summary>The extended attribute does not exist (NFS4ERR_NOXATTR).</summary>
    NoExtendedAttribute = 10095,

    /// <summary>The extended attribute value or set is too large (NFS4ERR_XATTR2BIG).</summary>
    ExtendedAttributeTooBig = 10096,
}
