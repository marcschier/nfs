using System.Buffers;

using Nfs.Rpc;
using Nfs.Xdr;

using Xunit;

namespace Nfs.Rpc.Tests;

public sealed class RpcMessageTests
{
    [Fact]
    public void OpaqueAuth_None_ProducesExpectedWireBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        OpaqueAuth.None.WriteTo(ref writer);

        // flavor AUTH_NONE (0) + zero-length body.
        Assert.Equal(Hex("00000000 00000000"), buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void OpaqueAuth_RoundTrips()
    {
        var original = new OpaqueAuth(AuthFlavor.Sys, new byte[] { 1, 2, 3, 4, 5 });

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        original.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        OpaqueAuth decoded = OpaqueAuth.ReadFrom(ref reader);

        Assert.Equal(AuthFlavor.Sys, decoded.Flavor);
        Assert.Equal(original.Body.ToArray(), decoded.Body.ToArray());
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void AuthSys_Create_EncodesAuthsysParms()
    {
        OpaqueAuth credential = AuthSys.Create(uid: 1000, gid: 1000, machineName: "m");

        Assert.Equal(AuthFlavor.Sys, credential.Flavor);
        Assert.Equal(
            Hex("00000000" +   // stamp = 0
                "00000001 6D000000" + // machinename "m" (len 1 + padding)
                "000003E8" +   // uid = 1000
                "000003E8" +   // gid = 1000
                "00000000"),   // 0 auxiliary gids
            credential.Body.ToArray());
    }

    [Fact]
    public void RpcCallHeader_ProducesExpectedWireBytes()
    {
        var header = new RpcCallHeader(
            Xid: 0x12345678,
            Program: 100003,
            Version: 3,
            Procedure: 1,
            Credential: OpaqueAuth.None,
            Verifier: OpaqueAuth.None);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        header.WriteTo(ref writer);

        Assert.Equal(
            Hex("12345678" +          // xid
                "00000000" +          // msg_type CALL
                "00000002" +          // rpcvers = 2
                "000186A3" +          // program 100003
                "00000003" +          // version 3
                "00000001" +          // procedure 1
                "00000000 00000000" + // credential AUTH_NONE
                "00000000 00000000"), // verifier AUTH_NONE
            buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void RpcCallHeader_RoundTrips_WithAuthSys()
    {
        var header = new RpcCallHeader(
            Xid: 42,
            Program: 100003,
            Version: 3,
            Procedure: 7,
            Credential: AuthSys.Create(1000, 1000, "host"),
            Verifier: OpaqueAuth.None);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        header.WriteTo(ref writer);

        var reader = new XdrReader(buffer.WrittenSpan);
        RpcCallHeader decoded = RpcCallHeader.ReadFrom(ref reader);

        Assert.Equal(header.Xid, decoded.Xid);
        Assert.Equal(header.Program, decoded.Program);
        Assert.Equal(header.Version, decoded.Version);
        Assert.Equal(header.Procedure, decoded.Procedure);
        Assert.Equal(AuthFlavor.Sys, decoded.Credential.Flavor);
        Assert.Equal(AuthFlavor.None, decoded.Verifier.Flavor);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void RpcReply_AcceptedSuccess_Decodes()
    {
        var reader = new XdrReader(Hex(
            "00000001" +          // xid
            "00000001" +          // msg_type REPLY
            "00000000" +          // reply_stat MSG_ACCEPTED
            "00000000 00000000" + // verifier AUTH_NONE
            "00000000"));         // accept_stat SUCCESS

        RpcReplyHeader reply = RpcReplyHeader.ReadFrom(ref reader);

        Assert.True(reply.IsSuccess);
        Assert.Equal(1u, reply.Xid);
        Assert.Equal(AcceptStatus.Success, reply.Accept);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact]
    public void RpcReply_ProgramMismatch_Decodes()
    {
        var reader = new XdrReader(Hex(
            "00000001" +          // xid
            "00000001" +          // REPLY
            "00000000" +          // MSG_ACCEPTED
            "00000000 00000000" + // verifier AUTH_NONE
            "00000002" +          // accept_stat PROG_MISMATCH
            "00000002" +          // low = 2
            "00000004"));         // high = 4

        RpcReplyHeader reply = RpcReplyHeader.ReadFrom(ref reader);

        Assert.False(reply.IsSuccess);
        Assert.Equal(AcceptStatus.ProgramMismatch, reply.Accept);
        Assert.Equal(2u, reply.MismatchLow);
        Assert.Equal(4u, reply.MismatchHigh);
    }

    [Fact]
    public void RpcReply_DeniedAuthError_Decodes()
    {
        var reader = new XdrReader(Hex(
            "0000002A" + // xid = 42
            "00000001" + // REPLY
            "00000001" + // reply_stat MSG_DENIED
            "00000001" + // reject_stat AUTH_ERROR
            "00000001")); // auth_stat AUTH_BADCRED

        RpcReplyHeader reply = RpcReplyHeader.ReadFrom(ref reader);

        Assert.False(reply.IsSuccess);
        Assert.Equal(ReplyStatus.Denied, reply.Status);
        Assert.Equal(RejectStatus.AuthError, reply.Reject);
        Assert.Equal(AuthStatus.BadCredential, reply.Auth);
    }

    [Fact]
    public void RpcReply_DeniedRpcMismatch_Decodes()
    {
        var reader = new XdrReader(Hex(
            "00000001" + // xid
            "00000001" + // REPLY
            "00000001" + // MSG_DENIED
            "00000000" + // reject_stat RPC_MISMATCH
            "00000002" + // low = 2
            "00000002")); // high = 2

        RpcReplyHeader reply = RpcReplyHeader.ReadFrom(ref reader);

        Assert.Equal(ReplyStatus.Denied, reply.Status);
        Assert.Equal(RejectStatus.RpcVersionMismatch, reply.Reject);
        Assert.Equal(2u, reply.MismatchLow);
        Assert.Equal(2u, reply.MismatchHigh);
    }

    private static byte[] Hex(string hex) =>
        Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
}
