using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for GETATTR (<c>GETATTR3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3GetAttrArgs
{
    /// <summary>The handle of the object whose attributes are requested.</summary>
    [XdrField(0)]
    public Nfs3Handle Handle { get; set; }
}

/// <summary>
/// The result of GETATTR (<c>GETATTR3res</c>, RFC 1813): the attributes on success, otherwise just
/// the status.
/// </summary>
public record struct Nfs3GetAttrResult : IXdrSerializable<Nfs3GetAttrResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The object's attributes (valid only when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3FileAttributes Attributes { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result carrying <paramref name="attributes"/>.</summary>
    /// <param name="attributes">The object's attributes.</param>
    /// <returns>The result.</returns>
    public static Nfs3GetAttrResult Success(Nfs3FileAttributes attributes) =>
        new() { Status = NfsStatus.Ok, Attributes = attributes };

    /// <summary>Creates a failed result with the given status.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3GetAttrResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == NfsStatus.Ok)
        {
            Attributes.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3GetAttrResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        var result = new Nfs3GetAttrResult { Status = status };
        if (status == NfsStatus.Ok)
        {
            result.Attributes = Nfs3FileAttributes.ReadFrom(ref reader);
        }

        return result;
    }
}
