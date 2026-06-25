using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// A decoded ONC/RPC reply header. After a successful decode the reader is positioned at the
/// procedure-specific results (when <see cref="IsSuccess"/> is <see langword="true"/>).
/// </summary>
public readonly record struct RpcReplyHeader
{
    /// <summary>Gets the transaction id correlating this reply with its call.</summary>
    public uint Xid { get; init; }

    /// <summary>Gets whether the call was accepted or denied.</summary>
    public ReplyStatus Status { get; init; }

    /// <summary>Gets the server's verifier (present only when the call was accepted).</summary>
    public OpaqueAuth Verifier { get; init; }

    /// <summary>Gets the disposition of an accepted call.</summary>
    public AcceptStatus Accept { get; init; }

    /// <summary>Gets the reason a denied call was rejected.</summary>
    public RejectStatus Reject { get; init; }

    /// <summary>Gets the authentication failure reason (when <see cref="Reject"/> is auth error).</summary>
    public AuthStatus Auth { get; init; }

    /// <summary>Gets the lowest version the server supports (for a version mismatch).</summary>
    public uint MismatchLow { get; init; }

    /// <summary>Gets the highest version the server supports (for a version mismatch).</summary>
    public uint MismatchHigh { get; init; }

    /// <summary>
    /// Gets a value indicating whether the call was accepted and completed successfully, in which
    /// case the reader is positioned at the procedure results.
    /// </summary>
    public bool IsSuccess => Status == ReplyStatus.Accepted && Accept == AcceptStatus.Success;

    /// <summary>Decodes an <see cref="RpcReplyHeader"/> from the supplied reader.</summary>
    /// <param name="reader">The reader to decode from.</param>
    /// <returns>The decoded reply header.</returns>
    /// <exception cref="RpcException">The message is not a reply.</exception>
    public static RpcReplyHeader ReadFrom(ref XdrReader reader)
    {
        uint xid = reader.ReadUInt32();

        var messageType = (MessageType)reader.ReadInt32();
        if (messageType != MessageType.Reply)
        {
            throw new RpcException($"Expected an RPC reply but found message type {messageType}.");
        }

        var status = (ReplyStatus)reader.ReadInt32();
        return status == ReplyStatus.Accepted
            ? ReadAccepted(ref reader, xid)
            : ReadDenied(ref reader, xid);
    }

    private static RpcReplyHeader ReadAccepted(ref XdrReader reader, uint xid)
    {
        OpaqueAuth verifier = OpaqueAuth.ReadFrom(ref reader);
        var accept = (AcceptStatus)reader.ReadInt32();

        uint low = 0;
        uint high = 0;
        if (accept == AcceptStatus.ProgramMismatch)
        {
            low = reader.ReadUInt32();
            high = reader.ReadUInt32();
        }

        return new RpcReplyHeader
        {
            Xid = xid,
            Status = ReplyStatus.Accepted,
            Verifier = verifier,
            Accept = accept,
            MismatchLow = low,
            MismatchHigh = high,
        };
    }

    private static RpcReplyHeader ReadDenied(ref XdrReader reader, uint xid)
    {
        var reject = (RejectStatus)reader.ReadInt32();
        if (reject == RejectStatus.RpcVersionMismatch)
        {
            uint low = reader.ReadUInt32();
            uint high = reader.ReadUInt32();
            return new RpcReplyHeader
            {
                Xid = xid,
                Status = ReplyStatus.Denied,
                Reject = reject,
                MismatchLow = low,
                MismatchHigh = high,
            };
        }

        var auth = (AuthStatus)reader.ReadInt32();
        return new RpcReplyHeader
        {
            Xid = xid,
            Status = ReplyStatus.Denied,
            Reject = reject,
            Auth = auth,
        };
    }
}
