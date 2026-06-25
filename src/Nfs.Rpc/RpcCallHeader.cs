using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// The header of an ONC/RPC call message: everything up to (but not including) the
/// procedure-specific arguments.
/// </summary>
/// <param name="Xid">The transaction id correlating this call with its reply.</param>
/// <param name="Program">The remote program number.</param>
/// <param name="Version">The remote program version.</param>
/// <param name="Procedure">The procedure number within the program.</param>
/// <param name="Credential">The caller's credential.</param>
/// <param name="Verifier">The caller's verifier.</param>
public readonly record struct RpcCallHeader(
    uint Xid,
    uint Program,
    uint Version,
    uint Procedure,
    OpaqueAuth Credential,
    OpaqueAuth Verifier)
{
    /// <summary>Encodes this call header into the supplied writer.</summary>
    /// <param name="writer">The writer to encode into.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32(Xid);
        writer.WriteInt32((int)MessageType.Call);
        writer.WriteUInt32(RpcConstants.RpcVersion);
        writer.WriteUInt32(Program);
        writer.WriteUInt32(Version);
        writer.WriteUInt32(Procedure);
        Credential.WriteTo(ref writer);
        Verifier.WriteTo(ref writer);
    }

    /// <summary>Decodes an <see cref="RpcCallHeader"/> from the supplied reader.</summary>
    /// <param name="reader">The reader to decode from.</param>
    /// <returns>The decoded call header.</returns>
    /// <exception cref="RpcException">
    /// The message is not a call, or it uses an unsupported RPC version.
    /// </exception>
    public static RpcCallHeader ReadFrom(ref XdrReader reader)
    {
        uint xid = reader.ReadUInt32();

        var messageType = (MessageType)reader.ReadInt32();
        if (messageType != MessageType.Call)
        {
            throw new RpcException($"Expected an RPC call but found message type {messageType}.");
        }

        uint rpcVersion = reader.ReadUInt32();
        if (rpcVersion != RpcConstants.RpcVersion)
        {
            throw new RpcException($"Unsupported RPC version {rpcVersion}; expected {RpcConstants.RpcVersion}.");
        }

        uint program = reader.ReadUInt32();
        uint version = reader.ReadUInt32();
        uint procedure = reader.ReadUInt32();
        OpaqueAuth credential = OpaqueAuth.ReadFrom(ref reader);
        OpaqueAuth verifier = OpaqueAuth.ReadFrom(ref reader);

        return new RpcCallHeader(xid, program, version, procedure, credential, verifier);
    }
}
