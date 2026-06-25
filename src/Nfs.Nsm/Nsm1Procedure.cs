namespace Nfs.Nsm;

/// <summary>Identifies the Network Status Monitor RPC program (NSM, <c>sm_inter</c>).</summary>
public static class Nsm1
{
    /// <summary>The NSM RPC program number.</summary>
    public const uint Program = 100024;

    /// <summary>The NSM protocol version number.</summary>
    public const uint ProtocolVersion = 1;

    /// <summary>The largest host name encoded by this implementation.</summary>
    public const int MaxNameLength = 1024;

    /// <summary>The fixed length of an NSM private cookie.</summary>
    public const int PrivateLength = 16;
}

/// <summary>The NSM version 1 procedure numbers.</summary>
public enum Nsm1Procedure
{
    /// <summary>Do nothing (<c>SM_NULL</c>).</summary>
    Null = 0,

    /// <summary>Query monitor status for a host (<c>SM_STAT</c>).</summary>
    Stat = 1,

    /// <summary>Register a host monitor (<c>SM_MON</c>).</summary>
    Monitor = 2,

    /// <summary>Unregister one host monitor (<c>SM_UNMON</c>).</summary>
    Unmonitor = 3,

    /// <summary>Unregister all host monitors for the caller (<c>SM_UNMON_ALL</c>).</summary>
    UnmonitorAll = 4,

    /// <summary>Simulate a local crash (<c>SM_SIMU_CRASH</c>).</summary>
    SimulateCrash = 5,

    /// <summary>Receive a remote host state change notification (<c>SM_NOTIFY</c>).</summary>
    Notify = 6,
}

/// <summary>The result code in an NSM status reply.</summary>
public enum Nsm1Result
{
    /// <summary>The operation succeeded (<c>STAT_SUCC</c>).</summary>
    Success = 0,

    /// <summary>The operation failed (<c>STAT_FAIL</c>).</summary>
    Failure = 1,
}
