using System.Buffers;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// Encodes and decodes ONC/RPC call and reply messages (RFC 5531). Shared by the TCP and UDP
/// transports so that framing is the only thing that differs between them.
/// </summary>
internal static class RpcMessageCodec
{
    /// <summary>Encodes a complete call message (header plus arguments).</summary>
    public static byte[] EncodeCall<TArgs>(
        uint xid,
        uint program,
        uint version,
        uint procedure,
        OpaqueAuth credential,
        OpaqueAuth verifier,
        TArgs arguments)
        where TArgs : IXdrSerializable<TArgs>
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        new RpcCallHeader(xid, program, version, procedure, credential, verifier).WriteTo(ref writer);
        arguments.WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes the portion of an RPC call header covered by RPCSEC_GSS call MICs.</summary>
    public static byte[] EncodeCallHeaderPrefix(RpcCallHeader header)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(header.Xid);
        writer.WriteInt32((int)MessageType.Call);
        writer.WriteUInt32(RpcConstants.RpcVersion);
        writer.WriteUInt32(header.Program);
        writer.WriteUInt32(header.Version);
        writer.WriteUInt32(header.Procedure);
        header.Credential.WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Parses a reply message and validates that its XID matches the expected one.</summary>
    public static RpcReply ParseReply(uint expectedXid, byte[] reply)
    {
        var reader = new XdrReader(reply);
        RpcReplyHeader header = RpcReplyHeader.ReadFrom(ref reader);
        if (header.Xid != expectedXid)
        {
            throw new RpcException($"Reply XID {header.Xid} does not match call XID {expectedXid}.");
        }

        var result = new ReadOnlyMemory<byte>(reply, reader.Position, reply.Length - reader.Position);
        return new RpcReply(header, result);
    }

    /// <summary>Reads only the XID from a reply message, without validating it.</summary>
    public static uint PeekReplyXid(ReadOnlySpan<byte> reply)
    {
        var reader = new XdrReader(reply);
        return reader.ReadUInt32();
    }

    /// <summary>Reads the RPC message type from a complete call or reply message.</summary>
    public static MessageType PeekMessageType(ReadOnlySpan<byte> message)
    {
        var reader = new XdrReader(message);
        _ = reader.ReadUInt32();
        return (MessageType)reader.ReadInt32();
    }

    /// <summary>Parses a call header and returns it along with the offset where arguments begin.</summary>
    public static (RpcCallHeader Header, int ArgumentsOffset) ParseCallHeader(byte[] message)
    {
        var reader = new XdrReader(message);
        RpcCallHeader header = RpcCallHeader.ReadFrom(ref reader);
        return (header, reader.Position);
    }

    /// <summary>Encodes a reply message for the given transaction id and payload.</summary>
    public static byte[] EncodeReply(uint xid, RpcReplyPayload payload) =>
        EncodeReply(xid, payload, OpaqueAuth.None);

    /// <summary>Encodes an accepted reply message for the given transaction id, verifier, and payload.</summary>
    public static byte[] EncodeReply(uint xid, RpcReplyPayload payload, OpaqueAuth verifier)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);

        writer.WriteUInt32(xid);
        writer.WriteInt32((int)MessageType.Reply);
        writer.WriteInt32((int)ReplyStatus.Accepted);
        verifier.WriteTo(ref writer);
        writer.WriteInt32((int)payload.Status);

        if (payload.Status == AcceptStatus.ProgramMismatch)
        {
            writer.WriteUInt32(payload.MismatchLow);
            writer.WriteUInt32(payload.MismatchHigh);
        }
        else if (payload.Status == AcceptStatus.Success && !payload.Result.IsEmpty)
        {
            writer.WriteRaw(payload.Result.Span);
        }

        return buffer.WrittenSpan.ToArray();
    }

    /// <summary>Encodes a denied reply carrying an authentication failure status.</summary>
    public static byte[] EncodeAuthError(uint xid, AuthStatus status)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(xid);
        writer.WriteInt32((int)MessageType.Reply);
        writer.WriteInt32((int)ReplyStatus.Denied);
        writer.WriteInt32((int)RejectStatus.AuthError);
        writer.WriteInt32((int)status);
        return buffer.WrittenSpan.ToArray();
    }
}
