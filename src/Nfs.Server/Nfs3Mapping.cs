using Nfs.Abstractions;
using Nfs.Protocol.V3;

namespace Nfs.Server;

/// <summary>Maps between the abstraction types and the NFS version 3 wire types.</summary>
internal static class Nfs3Mapping
{
    public static NfsFileHandle ToHandle(Nfs3Handle wire) => new(wire.Data);

    public static Nfs3Handle ToWire(NfsFileHandle handle) => new() { Data = handle.ToArray() };

    public static Nfs3Time ToWire(NfsTimestamp timestamp) =>
        new() { Seconds = timestamp.Seconds, Nanoseconds = timestamp.Nanoseconds };

    public static NfsTimestamp ToTimestamp(Nfs3Time time) => new(time.Seconds, time.Nanoseconds);

    public static NfsSetAttributes ToSetAttributes(Nfs3SetAttributes wire) => new()
    {
        Mode = wire.Mode,
        Uid = wire.Uid,
        Gid = wire.Gid,
        Size = wire.Size,
        AccessTime = wire.AccessTimeHow switch
        {
            Nfs3TimeHow.SetToServerTime => NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Nfs3TimeHow.SetToClientTime => ToTimestamp(wire.AccessTime),
            _ => null,
        },
        ModifyTime = wire.ModifyTimeHow switch
        {
            Nfs3TimeHow.SetToServerTime => NfsTimestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            Nfs3TimeHow.SetToClientTime => ToTimestamp(wire.ModifyTime),
            _ => null,
        },
    };

    public static Nfs3FileAttributes ToWire(NfsFileAttributes attributes) => new()
    {
        Type = attributes.Type,
        Mode = attributes.Mode,
        LinkCount = attributes.LinkCount,
        Uid = attributes.Uid,
        Gid = attributes.Gid,
        Size = attributes.Size,
        Used = attributes.Used,
        Rdev = default,
        FileSystemId = 0,
        FileId = attributes.FileId,
        AccessTime = ToWire(attributes.AccessTime),
        ModifyTime = ToWire(attributes.ModifyTime),
        ChangeTime = ToWire(attributes.ChangeTime),
    };
}
