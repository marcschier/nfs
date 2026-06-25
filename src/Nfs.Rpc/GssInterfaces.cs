namespace Nfs.Rpc;

/// <summary>Identifies an RPCSEC_GSS protection service.</summary>
public enum RpcSecGssService
{
    /// <summary>Authenticate the caller only; arguments and results are sent as plain XDR.</summary>
    None = 1,

    /// <summary>Authenticate the caller and protect arguments and results with a MIC.</summary>
    Integrity = 2,

    /// <summary>Authenticate the caller and encrypt arguments and results.</summary>
    Privacy = 3,
}

/// <summary>Identifies the RPCSEC_GSS control procedure carried by a credential.</summary>
public enum RpcSecGssProcedure
{
    /// <summary>A normal protected data call.</summary>
    Data = 0,

    /// <summary>The first context establishment call.</summary>
    Init = 1,

    /// <summary>A follow-up context establishment call.</summary>
    ContinueInit = 2,

    /// <summary>A context destruction call.</summary>
    Destroy = 3,
}

/// <summary>Common GSS-API major status values used by the RPCSEC_GSS handshake.</summary>
public enum GssMajorStatus : uint
{
    /// <summary>The token step completed successfully.</summary>
    Complete = 0,

    /// <summary>Another token exchange is required before the context is established.</summary>
    ContinueNeeded = 1,
}

/// <summary>The output of one GSS security context token step.</summary>
/// <param name="OutputToken">The token to send to the peer.</param>
/// <param name="MajorStatus">The GSS major status for the step.</param>
/// <param name="MinorStatus">The mechanism-specific minor status for the step.</param>
public readonly record struct GssTokenResult(
    ReadOnlyMemory<byte> OutputToken,
    GssMajorStatus MajorStatus,
    uint MinorStatus);

/// <summary>A pluggable GSS security context used by RPCSEC_GSS.</summary>
public interface IGssContext
{
    /// <summary>Gets a value indicating whether the context is established.</summary>
    bool IsEstablished { get; }

    /// <summary>Performs one initiator-side token step.</summary>
    /// <param name="inputToken">The peer token, or empty for the first step.</param>
    /// <returns>The token step result.</returns>
    GssTokenResult Init(ReadOnlySpan<byte> inputToken);

    /// <summary>Performs one acceptor-side token step.</summary>
    /// <param name="inputToken">The peer token.</param>
    /// <returns>The token step result.</returns>
    GssTokenResult Accept(ReadOnlySpan<byte> inputToken);

    /// <summary>Computes a MIC over <paramref name="message"/>.</summary>
    /// <param name="message">The message to authenticate.</param>
    /// <returns>The message MIC.</returns>
    byte[] GetMic(ReadOnlySpan<byte> message);

    /// <summary>Verifies <paramref name="mic"/> over <paramref name="message"/>.</summary>
    /// <param name="message">The authenticated message.</param>
    /// <param name="mic">The received MIC.</param>
    /// <returns><see langword="true"/> when the MIC is valid.</returns>
    bool VerifyMic(ReadOnlySpan<byte> message, ReadOnlySpan<byte> mic);

    /// <summary>Wraps <paramref name="message"/> for privacy.</summary>
    /// <param name="message">The plaintext message.</param>
    /// <returns>The wrapped message.</returns>
    byte[] Wrap(ReadOnlySpan<byte> message);

    /// <summary>Unwraps <paramref name="message"/> previously produced by <see cref="Wrap"/>.</summary>
    /// <param name="message">The wrapped message.</param>
    /// <returns>The plaintext message.</returns>
    byte[] Unwrap(ReadOnlySpan<byte> message);
}

/// <summary>Creates initiator and acceptor GSS security contexts.</summary>
public interface IGssMechanism
{
    /// <summary>Creates a client-side initiator context.</summary>
    /// <param name="targetName">The peer service principal or mechanism-specific target name.</param>
    /// <returns>A new initiator context.</returns>
    IGssContext CreateClientContext(string? targetName = null);

    /// <summary>Creates a server-side acceptor context.</summary>
    /// <returns>A new acceptor context.</returns>
    IGssContext CreateServerContext();
}

/// <summary>RPCSEC_GSS metadata attached to established DATA calls dispatched by <see cref="RpcServer"/>.</summary>
/// <param name="Handle">The server-assigned context handle.</param>
/// <param name="SequenceNumber">The RPCSEC_GSS sequence number for the call.</param>
/// <param name="Service">The protection service used by the call.</param>
/// <param name="Context">The established GSS context.</param>
public sealed record RpcSecGssCallContext(
    ReadOnlyMemory<byte> Handle,
    uint SequenceNumber,
    RpcSecGssService Service,
    IGssContext Context);
