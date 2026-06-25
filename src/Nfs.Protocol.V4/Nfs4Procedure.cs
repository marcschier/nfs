namespace Nfs.Protocol.V4;

/// <summary>Identifies the NFS version 4 RPC program and its limits (RFC 7530).</summary>
public static class Nfs4
{
    /// <summary>The NFS RPC program number (shared with versions 2 and 3).</summary>
    public const uint Program = 100003;

    /// <summary>The NFS version 4 protocol version number.</summary>
    public const uint ProtocolVersion = 4;

    /// <summary>The version 4.0 minor version.</summary>
    public const uint MinorVersion0 = 0;

    /// <summary>The version 4.1 minor version.</summary>
    public const uint MinorVersion1 = 1;

    /// <summary>The version 4.2 minor version.</summary>
    public const uint MinorVersion2 = 2;

    /// <summary>The size, in bytes, of a session identifier (<c>NFS4_SESSIONID_SIZE</c>).</summary>
    public const int SessionIdSize = 16;

    /// <summary>The largest file handle, in bytes (<c>NFS4_FHSIZE</c>).</summary>
    public const int MaxHandleSize = 128;

    /// <summary>The size, in bytes, of a verifier (<c>NFS4_VERIFIER_SIZE</c>).</summary>
    public const int VerifierSize = 8;

    /// <summary>The size, in bytes, of an opaque stateid "other" field.</summary>
    public const int OtherSize = 12;

    /// <summary>The largest READ or WRITE payload, in bytes, this implementation handles.</summary>
    public const int MaxIoSize = 1024 * 1024;

    /// <summary>The largest component or path name, in bytes.</summary>
    public const int MaxNameLength = 255;
}

/// <summary>The NFS version 4 procedure numbers (RFC 7530).</summary>
public enum Nfs4Procedure
{
    /// <summary>Do nothing (NFSPROC4_NULL).</summary>
    Null = 0,

    /// <summary>Execute a compound request (NFSPROC4_COMPOUND).</summary>
    Compound = 1,
}

/// <summary>The NFS version 4.0 operation numbers (<c>nfs_opnum4</c>, RFC 7530).</summary>
public enum Nfs4Op
{
    /// <summary>Check access rights (OP_ACCESS).</summary>
    Access = 3,

    /// <summary>Close a file (OP_CLOSE).</summary>
    Close = 4,

    /// <summary>Commit cached data (OP_COMMIT).</summary>
    Commit = 5,

    /// <summary>Create a non-regular file object (OP_CREATE).</summary>
    Create = 6,

    /// <summary>Purge delegations (OP_DELEGPURGE).</summary>
    DelegPurge = 7,

    /// <summary>Return a delegation (OP_DELEGRETURN).</summary>
    DelegReturn = 8,

    /// <summary>Get attributes (OP_GETATTR).</summary>
    GetAttr = 9,

    /// <summary>Get the current file handle (OP_GETFH).</summary>
    GetFh = 10,

    /// <summary>Create a hard link (OP_LINK).</summary>
    Link = 11,

    /// <summary>Create a byte-range lock (OP_LOCK).</summary>
    Lock = 12,

    /// <summary>Test for a byte-range lock (OP_LOCKT).</summary>
    LockTest = 13,

    /// <summary>Release a byte-range lock (OP_LOCKU).</summary>
    LockUnlock = 14,

    /// <summary>Look up a name (OP_LOOKUP).</summary>
    Lookup = 15,

    /// <summary>Look up the parent directory (OP_LOOKUPP).</summary>
    LookupParent = 16,

    /// <summary>Verify that attributes differ (OP_NVERIFY).</summary>
    NVerify = 17,

    /// <summary>Open a file (OP_OPEN).</summary>
    Open = 18,

    /// <summary>Open a named-attribute directory (OP_OPENATTR).</summary>
    OpenAttr = 19,

    /// <summary>Confirm an open (OP_OPEN_CONFIRM).</summary>
    OpenConfirm = 20,

    /// <summary>Downgrade an open's share access (OP_OPEN_DOWNGRADE).</summary>
    OpenDowngrade = 21,

    /// <summary>Set the current file handle (OP_PUTFH).</summary>
    PutFh = 22,

    /// <summary>Set the current file handle to the public handle (OP_PUTPUBFH).</summary>
    PutPubFh = 23,

    /// <summary>Set the current file handle to the root (OP_PUTROOTFH).</summary>
    PutRootFh = 24,

    /// <summary>Read from a file (OP_READ).</summary>
    Read = 25,

    /// <summary>Read a directory (OP_READDIR).</summary>
    ReadDir = 26,

    /// <summary>Read a symbolic link (OP_READLINK).</summary>
    ReadLink = 27,

    /// <summary>Remove a file (OP_REMOVE).</summary>
    Remove = 28,

    /// <summary>Rename a file (OP_RENAME).</summary>
    Rename = 29,

    /// <summary>Renew a lease (OP_RENEW).</summary>
    Renew = 30,

    /// <summary>Restore the saved file handle (OP_RESTOREFH).</summary>
    RestoreFh = 31,

    /// <summary>Save the current file handle (OP_SAVEFH).</summary>
    SaveFh = 32,

    /// <summary>Obtain security information (OP_SECINFO).</summary>
    SecInfo = 33,

    /// <summary>Set attributes (OP_SETATTR).</summary>
    SetAttr = 34,

    /// <summary>Establish a client identifier (OP_SETCLIENTID).</summary>
    SetClientId = 35,

    /// <summary>Confirm a client identifier (OP_SETCLIENTID_CONFIRM).</summary>
    SetClientIdConfirm = 36,

    /// <summary>Verify that attributes match (OP_VERIFY).</summary>
    Verify = 37,

    /// <summary>Write to a file (OP_WRITE).</summary>
    Write = 38,

    /// <summary>Release a lock owner's state (OP_RELEASE_LOCKOWNER).</summary>
    ReleaseLockOwner = 39,

    /// <summary>Establish or update a client identifier, version 4.1 (OP_EXCHANGE_ID).</summary>
    ExchangeId = 42,

    /// <summary>Create a session, version 4.1 (OP_CREATE_SESSION).</summary>
    CreateSession = 43,

    /// <summary>Destroy a session, version 4.1 (OP_DESTROY_SESSION).</summary>
    DestroySession = 44,

    /// <summary>Get a pNFS device address, version 4.1 (OP_GETDEVICEINFO).</summary>
    GetDeviceInfo = 47,

    /// <summary>Commit a pNFS layout update, version 4.1 (OP_LAYOUTCOMMIT).</summary>
    LayoutCommit = 49,

    /// <summary>Get a pNFS layout, version 4.1 (OP_LAYOUTGET).</summary>
    LayoutGet = 50,

    /// <summary>Return a pNFS layout, version 4.1 (OP_LAYOUTRETURN).</summary>
    LayoutReturn = 51,

    /// <summary>Obtain security information for the current file handle, version 4.1 (OP_SECINFO_NO_NAME).</summary>
    SecInfoNoName = 52,

    /// <summary>Lead a version 4.1 COMPOUND with session sequencing (OP_SEQUENCE).</summary>
    Sequence = 53,

    /// <summary>Destroy a client identifier, version 4.1 (OP_DESTROY_CLIENTID).</summary>
    DestroyClientId = 57,

    /// <summary>Signal the end of reclaim after a reboot, version 4.1 (OP_RECLAIM_COMPLETE).</summary>
    ReclaimComplete = 58,

    /// <summary>Reserve space in a file, version 4.2 (OP_ALLOCATE).</summary>
    Allocate = 59,

    /// <summary>Copy data between two files on the server, version 4.2 (OP_COPY).</summary>
    Copy = 60,

    /// <summary>Authorize a destination server to copy from the current file (OP_COPY_NOTIFY).</summary>
    CopyNotify = 61,

    /// <summary>Free space in a file, version 4.2 (OP_DEALLOCATE).</summary>
    Deallocate = 62,

    /// <summary>Cancel an asynchronous server-side copy (OP_OFFLOAD_CANCEL).</summary>
    OffloadCancel = 66,

    /// <summary>Query an asynchronous server-side copy (OP_OFFLOAD_STATUS).</summary>
    OffloadStatus = 67,

    /// <summary>Read data and sparse-file metadata, version 4.2 (OP_READ_PLUS).</summary>
    ReadPlus = 68,

    /// <summary>Find the next data or hole in a file, version 4.2 (OP_SEEK).</summary>
    Seek = 69,

    /// <summary>Clone data between two files on the server, version 4.2 (OP_CLONE).</summary>
    Clone = 71,

    /// <summary>Get an extended attribute, RFC 8276 (OP_GETXATTR).</summary>
    GetXattr = 72,

    /// <summary>Set an extended attribute, RFC 8276 (OP_SETXATTR).</summary>
    SetXattr = 73,

    /// <summary>List extended attributes, RFC 8276 (OP_LISTXATTRS).</summary>
    ListXattrs = 74,

    /// <summary>Remove an extended attribute, RFC 8276 (OP_REMOVEXATTR).</summary>
    RemoveXattr = 75,

    /// <summary>A reserved illegal operation (OP_ILLEGAL).</summary>
    Illegal = 10044,
}

/// <summary>The object type of a version 4 file-system object (<c>nfs_ftype4</c>, RFC 7530).</summary>
public enum Nfs4FileType
{
    /// <summary>A regular file (NF4REG).</summary>
    Regular = 1,

    /// <summary>A directory (NF4DIR).</summary>
    Directory = 2,

    /// <summary>A block special device (NF4BLK).</summary>
    BlockDevice = 3,

    /// <summary>A character special device (NF4CHR).</summary>
    CharacterDevice = 4,

    /// <summary>A symbolic link (NF4LNK).</summary>
    SymbolicLink = 5,

    /// <summary>A socket (NF4SOCK).</summary>
    Socket = 6,

    /// <summary>A named pipe / FIFO (NF4FIFO).</summary>
    Fifo = 7,

    /// <summary>An attribute directory (NF4ATTRDIR).</summary>
    AttributeDirectory = 8,

    /// <summary>A named attribute (NF4NAMEDATTR).</summary>
    NamedAttribute = 9,
}
