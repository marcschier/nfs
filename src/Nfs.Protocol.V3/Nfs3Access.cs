using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>The access-permission bits checked by the ACCESS procedure (RFC 1813).</summary>
public static class Nfs3Access
{
    /// <summary>Read data from a file or read a directory (ACCESS3_READ).</summary>
    public const uint Read = 0x0001;

    /// <summary>Look up a name in a directory (ACCESS3_LOOKUP).</summary>
    public const uint Lookup = 0x0002;

    /// <summary>Rewrite existing file data or modify a directory (ACCESS3_MODIFY).</summary>
    public const uint Modify = 0x0004;

    /// <summary>Extend a file or add an entry to a directory (ACCESS3_EXTEND).</summary>
    public const uint Extend = 0x0008;

    /// <summary>Delete an entry from a directory (ACCESS3_DELETE).</summary>
    public const uint Delete = 0x0010;

    /// <summary>Execute a file or traverse a directory (ACCESS3_EXECUTE).</summary>
    public const uint Execute = 0x0020;

    /// <summary>Every defined access bit.</summary>
    public const uint All = Read | Lookup | Modify | Extend | Delete | Execute;
}

/// <summary>Arguments for ACCESS (<c>ACCESS3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3AccessArgs
{
    /// <summary>The object handle.</summary>
    [XdrField(0)]
    public Nfs3Handle Handle { get; set; }

    /// <summary>The access bits to check.</summary>
    [XdrField(1)]
    public uint Access { get; set; }
}

/// <summary>The success arm of ACCESS (<c>ACCESS3resok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3AccessResultOk
{
    /// <summary>The object's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? ObjectAttributes { get; set; }

    /// <summary>The subset of the requested bits that are actually permitted.</summary>
    [XdrField(1)]
    public uint Access { get; set; }
}

/// <summary>The failure arm of ACCESS (<c>ACCESS3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3AccessResultFail
{
    /// <summary>The object's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? ObjectAttributes { get; set; }
}

/// <summary>The result of ACCESS (<c>ACCESS3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3AccessResult : IXdrSerializable<Nfs3AccessResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3AccessResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3AccessResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the call succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3AccessResult Success(Nfs3AccessResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3AccessResult Failure(NfsStatus status, Nfs3AccessResultFail fail = default) =>
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
    public static Nfs3AccessResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3AccessResult { Status = status, Ok = Nfs3AccessResultOk.ReadFrom(ref reader) }
            : new Nfs3AccessResult { Status = status, Fail = Nfs3AccessResultFail.ReadFrom(ref reader) };
    }
}
