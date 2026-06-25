namespace Nfs.Rpc;

/// <summary>Distinguishes an RPC call from a reply.</summary>
public enum MessageType
{
    /// <summary>A call from a client to a server.</summary>
    Call = 0,

    /// <summary>A reply from a server to a client.</summary>
    Reply = 1,
}

/// <summary>Whether a server accepted or denied an RPC call.</summary>
public enum ReplyStatus
{
    /// <summary>The call was accepted (it may still have failed at the application level).</summary>
    Accepted = 0,

    /// <summary>The call was denied (an RPC-level or authentication failure).</summary>
    Denied = 1,
}

/// <summary>The disposition of an accepted RPC call.</summary>
public enum AcceptStatus
{
    /// <summary>The call completed and results follow.</summary>
    Success = 0,

    /// <summary>The requested program is not available on the server.</summary>
    ProgramUnavailable = 1,

    /// <summary>The program does not support the requested version.</summary>
    ProgramMismatch = 2,

    /// <summary>The program does not support the requested procedure.</summary>
    ProcedureUnavailable = 3,

    /// <summary>The arguments could not be decoded.</summary>
    GarbageArguments = 4,

    /// <summary>The server encountered an internal error.</summary>
    SystemError = 5,
}

/// <summary>Why a server denied an RPC call.</summary>
public enum RejectStatus
{
    /// <summary>The server does not support the client's RPC protocol version.</summary>
    RpcVersionMismatch = 0,

    /// <summary>The call failed authentication.</summary>
    AuthError = 1,
}

/// <summary>The reason an RPC call failed authentication.</summary>
public enum AuthStatus
{
    /// <summary>Authentication succeeded.</summary>
    Ok = 0,

    /// <summary>The credential was malformed (seal broken).</summary>
    BadCredential = 1,

    /// <summary>The server rejected the credential (for example an expired session).</summary>
    RejectedCredential = 2,

    /// <summary>The verifier was malformed (seal broken).</summary>
    BadVerifier = 3,

    /// <summary>The server rejected the verifier (for example a replayed credential).</summary>
    RejectedVerifier = 4,

    /// <summary>The credential is too weak for the server's policy.</summary>
    TooWeak = 5,

    /// <summary>The response verifier was invalid.</summary>
    InvalidResponse = 6,

    /// <summary>Authentication failed for an unspecified reason.</summary>
    Failed = 7,
}

/// <summary>The RPC authentication flavors supported by this stack.</summary>
public enum AuthFlavor
{
    /// <summary>No authentication (AUTH_NONE / AUTH_NULL).</summary>
    None = 0,

    /// <summary>Unix-style uid/gid authentication (AUTH_SYS / AUTH_UNIX).</summary>
    Sys = 1,

    /// <summary>RPCSEC_GSS security context authentication (RFC 2203).</summary>
    RpcSecGss = 6,
}
