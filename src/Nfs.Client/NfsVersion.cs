namespace Nfs.Client;

/// <summary>The NFS protocol version selected for a high-level client session.</summary>
public enum NfsVersion
{
    /// <summary>NFS version 2.</summary>
    V2 = 2,

    /// <summary>NFS version 3.</summary>
    V3 = 3,

    /// <summary>NFS version 4.0.</summary>
    V4 = 4,
}
