using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for LOOKUP (<c>LOOKUP3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3LookupArgs
{
    /// <summary>The directory and name to resolve.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs What { get; set; }
}

/// <summary>The success arm of LOOKUP (<c>LOOKUP3resok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3LookupResultOk
{
    /// <summary>The handle of the resolved object.</summary>
    [XdrField(0)]
    public Nfs3Handle Handle { get; set; }

    /// <summary>The resolved object's attributes, if available.</summary>
    [XdrField(1)]
    public Nfs3FileAttributes? ObjectAttributes { get; set; }

    /// <summary>The directory's attributes, if available.</summary>
    [XdrField(2)]
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }
}

/// <summary>The failure arm of LOOKUP (<c>LOOKUP3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3LookupResultFail
{
    /// <summary>The directory's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? DirectoryAttributes { get; set; }
}

/// <summary>The result of LOOKUP (<c>LOOKUP3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3LookupResult : IXdrSerializable<Nfs3LookupResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid only when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3LookupResultOk Ok { get; set; }

    /// <summary>The failure data (valid only when <see cref="Status"/> is not <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3LookupResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the lookup succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3LookupResult Success(Nfs3LookupResultOk ok) =>
        new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3LookupResult Failure(NfsStatus status, Nfs3LookupResultFail fail = default) =>
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
    public static Nfs3LookupResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3LookupResult { Status = status, Ok = Nfs3LookupResultOk.ReadFrom(ref reader) }
            : new Nfs3LookupResult { Status = status, Fail = Nfs3LookupResultFail.ReadFrom(ref reader) };
    }
}
