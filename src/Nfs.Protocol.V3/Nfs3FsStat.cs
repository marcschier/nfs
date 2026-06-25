using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for FSSTAT (<c>FSSTAT3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3FsStatArgs
{
    /// <summary>A handle within the file system (typically the export root).</summary>
    [XdrField(0)]
    public Nfs3Handle FileSystemRoot { get; set; }
}

/// <summary>The result of FSSTAT (<c>FSSTAT3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3FsStatResult : IXdrSerializable<Nfs3FsStatResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The root object's attributes, if available.</summary>
    public Nfs3FileAttributes? Attributes { get; set; }

    /// <summary>The total size of the file system, in bytes.</summary>
    public ulong TotalBytes { get; set; }

    /// <summary>The amount of free space, in bytes.</summary>
    public ulong FreeBytes { get; set; }

    /// <summary>The free space available to an unprivileged user, in bytes.</summary>
    public ulong AvailableBytes { get; set; }

    /// <summary>The total number of objects the file system can hold.</summary>
    public ulong TotalFiles { get; set; }

    /// <summary>The number of free object slots.</summary>
    public ulong FreeFiles { get; set; }

    /// <summary>The number of free object slots available to an unprivileged user.</summary>
    public ulong AvailableFiles { get; set; }

    /// <summary>A hint, in seconds, for how long the volatile values remain accurate.</summary>
    public uint InvariantSeconds { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3FsStatResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Attributes.HasValue)
        {
            writer.WriteBool(true);
            Attributes.Value.WriteTo(ref writer);
        }
        else
        {
            writer.WriteBool(false);
        }

        if (Status == NfsStatus.Ok)
        {
            writer.WriteUInt64(TotalBytes);
            writer.WriteUInt64(FreeBytes);
            writer.WriteUInt64(AvailableBytes);
            writer.WriteUInt64(TotalFiles);
            writer.WriteUInt64(FreeFiles);
            writer.WriteUInt64(AvailableFiles);
            writer.WriteUInt32(InvariantSeconds);
        }
    }

    /// <inheritdoc/>
    public static Nfs3FsStatResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        Nfs3FileAttributes? attributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
        var result = new Nfs3FsStatResult { Status = status, Attributes = attributes };
        if (status == NfsStatus.Ok)
        {
            result.TotalBytes = reader.ReadUInt64();
            result.FreeBytes = reader.ReadUInt64();
            result.AvailableBytes = reader.ReadUInt64();
            result.TotalFiles = reader.ReadUInt64();
            result.FreeFiles = reader.ReadUInt64();
            result.AvailableFiles = reader.ReadUInt64();
            result.InvariantSeconds = reader.ReadUInt32();
        }

        return result;
    }
}
