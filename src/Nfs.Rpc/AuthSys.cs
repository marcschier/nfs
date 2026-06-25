using System.Buffers;

using Nfs.Xdr;

namespace Nfs.Rpc;

/// <summary>
/// Builds AUTH_SYS (a.k.a. AUTH_UNIX) credentials, which convey a caller's Unix identity
/// (RFC 5531 §8.2).
/// </summary>
public static class AuthSys
{
    /// <summary>
    /// Creates an AUTH_SYS credential whose body is the encoded <c>authsys_parms</c> structure.
    /// </summary>
    /// <param name="uid">The caller's user id.</param>
    /// <param name="gid">The caller's primary group id.</param>
    /// <param name="machineName">The caller's host name (at most 255 bytes once encoded).</param>
    /// <param name="auxiliaryGids">The caller's supplementary group ids (at most 16).</param>
    /// <param name="stamp">An arbitrary value the server echoes; defaults to 0.</param>
    /// <returns>An <see cref="OpaqueAuth"/> with flavor <see cref="AuthFlavor.Sys"/>.</returns>
    public static OpaqueAuth Create(
        uint uid,
        uint gid,
        string machineName,
        ReadOnlySpan<uint> auxiliaryGids = default,
        uint stamp = 0)
    {
        ArgumentNullException.ThrowIfNull(machineName);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        writer.WriteUInt32(stamp);
        writer.WriteString(machineName);
        writer.WriteUInt32(uid);
        writer.WriteUInt32(gid);
        writer.WriteUInt32((uint)auxiliaryGids.Length);
        foreach (uint groupId in auxiliaryGids)
        {
            writer.WriteUInt32(groupId);
        }

        return new OpaqueAuth(AuthFlavor.Sys, buffer.WrittenSpan.ToArray());
    }
}
