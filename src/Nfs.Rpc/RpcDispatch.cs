using System.Net;

namespace Nfs.Rpc;

/// <summary>
/// A program that an <see cref="RpcServer"/> can dispatch calls to. An implementation routes on the
/// requested version and procedure, decodes its arguments synchronously, performs its work, and
/// returns an encoded reply payload.
/// </summary>
public interface IRpcProgram
{
    /// <summary>Gets the RPC program number this implementation serves.</summary>
    uint Program { get; }

    /// <summary>Handles a single call.</summary>
    /// <param name="request">The call's version, procedure, and authentication.</param>
    /// <param name="arguments">The undecoded, procedure-specific argument bytes.</param>
    /// <param name="cancellationToken">A token to cancel the work.</param>
    /// <returns>The reply payload to return to the caller.</returns>
    ValueTask<RpcReplyPayload> InvokeAsync(
        RpcCallInfo request,
        ReadOnlyMemory<byte> arguments,
        CancellationToken cancellationToken);
}

/// <summary>Allows an RPC program to learn which optional RPC authentication services are enabled.</summary>
public interface IRpcSecurityAware
{
    /// <summary>Notifies the program whether RPCSEC_GSS calls can be accepted by the server.</summary>
    /// <param name="enabled">Whether RPCSEC_GSS is enabled.</param>
    void SetRpcSecGssEnabled(bool enabled);
}

/// <summary>Allows an RPC program to learn the endpoint bound by its hosting server.</summary>
public interface IRpcLocalEndPointAware
{
    /// <summary>Notifies the program which local endpoint accepts calls for it.</summary>
    /// <param name="endPoint">The bound local endpoint.</param>
    void SetRpcLocalEndPoint(IPEndPoint endPoint);
}

/// <summary>Identifies a single RPC call to an <see cref="IRpcProgram"/>.</summary>
/// <param name="Xid">The transaction id.</param>
/// <param name="Version">The requested program version.</param>
/// <param name="Procedure">The requested procedure number.</param>
/// <param name="Credential">The caller's credential.</param>
/// <param name="Verifier">The caller's verifier.</param>
/// <param name="Connection">The TCP connection that carried the call, if known.</param>
public readonly record struct RpcCallInfo(
    uint Xid,
    uint Version,
    uint Procedure,
    OpaqueAuth Credential,
    OpaqueAuth Verifier,
    RpcDuplexConnection? Connection = null)
{
    /// <summary>Gets RPCSEC_GSS context metadata for an authenticated DATA call, if present.</summary>
    public RpcSecGssCallContext? RpcSecGss { get; init; }
}

/// <summary>
/// The outcome of an <see cref="IRpcProgram.InvokeAsync"/> call: an accept status plus, on success,
/// the encoded procedure results.
/// </summary>
public readonly record struct RpcReplyPayload
{
    /// <summary>Gets the accept status returned to the caller.</summary>
    public AcceptStatus Status { get; private init; }

    /// <summary>Gets the encoded procedure results (present only when <see cref="Status"/> is success).</summary>
    public ReadOnlyMemory<byte> Result { get; private init; }

    /// <summary>Gets the lowest supported version (for a program mismatch).</summary>
    public uint MismatchLow { get; private init; }

    /// <summary>Gets the highest supported version (for a program mismatch).</summary>
    public uint MismatchHigh { get; private init; }

    /// <summary>Creates a successful reply carrying the encoded <paramref name="result"/>.</summary>
    /// <param name="result">The encoded procedure results.</param>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload Success(ReadOnlyMemory<byte> result) =>
        new() { Status = AcceptStatus.Success, Result = result };

    /// <summary>Creates a reply indicating the requested procedure is not available.</summary>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload ProcedureUnavailable() =>
        new() { Status = AcceptStatus.ProcedureUnavailable };

    /// <summary>Creates a reply indicating the requested version is not supported.</summary>
    /// <param name="low">The lowest supported version.</param>
    /// <param name="high">The highest supported version.</param>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload ProgramMismatch(uint low, uint high) =>
        new() { Status = AcceptStatus.ProgramMismatch, MismatchLow = low, MismatchHigh = high };

    /// <summary>Creates a reply indicating the arguments could not be decoded.</summary>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload GarbageArguments() =>
        new() { Status = AcceptStatus.GarbageArguments };

    /// <summary>Creates a reply indicating the server failed internally.</summary>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload SystemError() =>
        new() { Status = AcceptStatus.SystemError };

    /// <summary>Creates a reply indicating the requested program is not available.</summary>
    /// <returns>The reply payload.</returns>
    public static RpcReplyPayload ProgramUnavailable() =>
        new() { Status = AcceptStatus.ProgramUnavailable };
}
