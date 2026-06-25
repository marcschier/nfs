namespace Nfs.Nlm;

/// <summary>Identifies the Network Lock Manager RPC program (NLM, X/Open).</summary>
public static class Nlm4
{
    /// <summary>The NLM RPC program number.</summary>
    public const uint Program = 100021;

    /// <summary>The NLM version 4 protocol version number (64-bit offsets).</summary>
    public const uint ProtocolVersion = 4;

    /// <summary>The largest network object (handle/owner/cookie), in bytes.</summary>
    public const int MaxNetObject = 1024;

    /// <summary>The largest caller name, in bytes.</summary>
    public const int MaxStringLength = 1024;
}

/// <summary>The NLM version 4 procedure numbers (X/Open).</summary>
public enum Nlm4Procedure
{
    /// <summary>Do nothing (NLMPROC4_NULL).</summary>
    Null = 0,

    /// <summary>Test whether a lock could be granted (NLMPROC4_TEST).</summary>
    Test = 1,

    /// <summary>Acquire a lock (NLMPROC4_LOCK).</summary>
    Lock = 2,

    /// <summary>Cancel an outstanding blocked lock (NLMPROC4_CANCEL).</summary>
    Cancel = 3,

    /// <summary>Release a lock (NLMPROC4_UNLOCK).</summary>
    Unlock = 4,

    /// <summary>A server-to-client grant of a previously blocked lock (NLMPROC4_GRANTED).</summary>
    Granted = 5,
}

/// <summary>The result status of an NLM operation (<c>nlm4_stats</c>, X/Open).</summary>
public enum Nlm4Status
{
    /// <summary>The lock was granted (NLM4_GRANTED).</summary>
    Granted = 0,

    /// <summary>The lock conflicts with an existing lock (NLM4_DENIED).</summary>
    Denied = 1,

    /// <summary>The server is out of lock resources (NLM4_DENIED_NOLOCKS).</summary>
    DeniedNoLocks = 2,

    /// <summary>The blocking lock request is pending (NLM4_BLOCKED).</summary>
    Blocked = 3,

    /// <summary>The server is in its grace period (NLM4_DENIED_GRACE_PERIOD).</summary>
    DeniedGracePeriod = 4,

    /// <summary>A deadlock was detected (NLM4_DEADLCK).</summary>
    Deadlock = 5,

    /// <summary>The object is read-only (NLM4_ROFS).</summary>
    ReadOnly = 6,

    /// <summary>The file handle is stale (NLM4_STALE_FH).</summary>
    StaleHandle = 7,

    /// <summary>The lock range is too large (NLM4_FBIG).</summary>
    TooBig = 8,

    /// <summary>The server failed internally (NLM4_FAILED).</summary>
    Failed = 9,
}
