using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>The file-system property bits reported by FSINFO (RFC 1813).</summary>
public static class Nfs3FsProperties
{
    /// <summary>The file system supports hard links (FSF3_LINK).</summary>
    public const uint Link = 0x0001;

    /// <summary>The file system supports symbolic links (FSF3_SYMLINK).</summary>
    public const uint SymbolicLink = 0x0002;

    /// <summary>Path names are interpreted uniformly across the file system (FSF3_HOMOGENEOUS).</summary>
    public const uint Homogeneous = 0x0008;

    /// <summary>The server can set the exact time on a SETATTR (FSF3_CANSETTIME).</summary>
    public const uint CanSetTime = 0x0010;
}

/// <summary>Arguments for FSINFO (<c>FSINFO3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3FsInfoArgs
{
    /// <summary>A handle within the file system (typically the export root).</summary>
    [XdrField(0)]
    public Nfs3Handle FileSystemRoot { get; set; }
}

/// <summary>The success arm of FSINFO (<c>FSINFO3resok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3FsInfoResultOk
{
    /// <summary>The root's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? Attributes { get; set; }

    /// <summary>The maximum READ size the server supports.</summary>
    [XdrField(1)]
    public uint ReadMax { get; set; }

    /// <summary>The preferred READ size.</summary>
    [XdrField(2)]
    public uint ReadPreferred { get; set; }

    /// <summary>The suggested READ size multiple.</summary>
    [XdrField(3)]
    public uint ReadMultiple { get; set; }

    /// <summary>The maximum WRITE size the server supports.</summary>
    [XdrField(4)]
    public uint WriteMax { get; set; }

    /// <summary>The preferred WRITE size.</summary>
    [XdrField(5)]
    public uint WritePreferred { get; set; }

    /// <summary>The suggested WRITE size multiple.</summary>
    [XdrField(6)]
    public uint WriteMultiple { get; set; }

    /// <summary>The preferred READDIR size.</summary>
    [XdrField(7)]
    public uint DirectoryPreferred { get; set; }

    /// <summary>The maximum size of a file on the server.</summary>
    [XdrField(8)]
    public ulong MaxFileSize { get; set; }

    /// <summary>The server's time granularity.</summary>
    [XdrField(9)]
    public Nfs3Time TimeDelta { get; set; }

    /// <summary>The file-system property bits (see <see cref="Nfs3FsProperties"/>).</summary>
    [XdrField(10)]
    public uint Properties { get; set; }
}

/// <summary>The failure arm of FSINFO (<c>FSINFO3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3FsInfoResultFail
{
    /// <summary>The root's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? Attributes { get; set; }
}

/// <summary>The result of FSINFO (<c>FSINFO3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3FsInfoResult : IXdrSerializable<Nfs3FsInfoResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3FsInfoResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3FsInfoResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the call succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3FsInfoResult Success(Nfs3FsInfoResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3FsInfoResult Failure(NfsStatus status, Nfs3FsInfoResultFail fail = default) =>
        new() { Status = status, Fail = fail };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Ok.WriteTo(ref writer);
        }
        else
        {
            Fail.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3FsInfoResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3FsInfoResult { Status = status, Ok = Nfs3FsInfoResultOk.ReadFrom(ref reader) }
            : new Nfs3FsInfoResult { Status = status, Fail = Nfs3FsInfoResultFail.ReadFrom(ref reader) };
    }
}
