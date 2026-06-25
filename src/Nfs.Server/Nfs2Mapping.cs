using System.Buffers.Binary;

using Nfs.Abstractions;
using Nfs.Protocol.V2;

namespace Nfs.Server;

/// <summary>Maps between the abstraction types and the NFS version 2 wire types.</summary>
internal static class Nfs2Mapping
{
    /// <summary>
    /// Decodes a backend handle from a fixed 32-byte version 2 handle. The first four bytes hold
    /// the big-endian length of the backend handle; the rest is the handle followed by zero padding.
    /// </summary>
    /// <param name="wire">The version 2 handle.</param>
    /// <returns>The backend handle.</returns>
    public static NfsFileHandle ToHandle(Nfs2Handle wire)
    {
        byte[] data = wire.Data ?? [];
        if (data.Length < 4)
        {
            throw new NfsException(NfsStatus.BadHandle);
        }

        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
        if (length < 0 || length > data.Length - 4)
        {
            throw new NfsException(NfsStatus.BadHandle);
        }

        return new NfsFileHandle(data.AsSpan(4, length));
    }

    /// <summary>Encodes a backend handle into a fixed 32-byte version 2 handle.</summary>
    /// <param name="handle">The backend handle (must fit in 28 bytes).</param>
    /// <returns>The version 2 handle.</returns>
    public static Nfs2Handle ToWire(NfsFileHandle handle)
    {
        byte[] data = new byte[Nfs2.HandleSize];
        ReadOnlySpan<byte> source = handle.Span;
        if (source.Length > Nfs2.HandleSize - 4)
        {
            throw new NfsException(NfsStatus.ServerFault);
        }

        BinaryPrimitives.WriteUInt32BigEndian(data, (uint)source.Length);
        source.CopyTo(data.AsSpan(4));
        return new Nfs2Handle { Data = data };
    }

    public static Nfs2Time ToWire(NfsTimestamp timestamp) =>
        new() { Seconds = timestamp.Seconds, MicroSeconds = timestamp.Nanoseconds / 1000 };

    public static Nfs2FileAttributes ToWire(NfsFileAttributes attributes) => new()
    {
        Type = attributes.Type,
        Mode = attributes.Mode,
        LinkCount = attributes.LinkCount,
        Uid = attributes.Uid,
        Gid = attributes.Gid,
        Size = (uint)Math.Min(attributes.Size, uint.MaxValue),
        BlockSize = 4096,
        Rdev = 0,
        Blocks = (uint)Math.Min((attributes.Used + 511) / 512, uint.MaxValue),
        FileSystemId = 0,
        FileId = (uint)attributes.FileId,
        AccessTime = ToWire(attributes.AccessTime),
        ModifyTime = ToWire(attributes.ModifyTime),
        ChangeTime = ToWire(attributes.ChangeTime),
    };

    public static NfsSetAttributes ToSetAttributes(Nfs2SetAttributes wire) => new()
    {
        Mode = wire.Mode == Nfs2.Unchanged ? null : wire.Mode,
        Uid = wire.Uid == Nfs2.Unchanged ? null : wire.Uid,
        Gid = wire.Gid == Nfs2.Unchanged ? null : wire.Gid,
        Size = wire.Size == Nfs2.Unchanged ? null : wire.Size,
        AccessTime = wire.AccessTime.Seconds == Nfs2.Unchanged
            ? null
            : new NfsTimestamp(wire.AccessTime.Seconds, wire.AccessTime.MicroSeconds * 1000),
        ModifyTime = wire.ModifyTime.Seconds == Nfs2.Unchanged
            ? null
            : new NfsTimestamp(wire.ModifyTime.Seconds, wire.ModifyTime.MicroSeconds * 1000),
    };

    /// <summary>Encodes a directory ordinal into a 4-byte version 2 cookie.</summary>
    /// <param name="ordinal">The 1-based ordinal of the entry.</param>
    /// <returns>The cookie bytes.</returns>
    public static byte[] ToCookie(uint ordinal)
    {
        byte[] cookie = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(cookie, ordinal);
        return cookie;
    }

    /// <summary>Decodes a 4-byte version 2 cookie into a directory ordinal.</summary>
    /// <param name="cookie">The cookie bytes.</param>
    /// <returns>The 1-based ordinal, or 0 to start at the beginning.</returns>
    public static uint FromCookie(byte[]? cookie) =>
        cookie is { Length: >= 4 } ? BinaryPrimitives.ReadUInt32BigEndian(cookie) : 0;
}
