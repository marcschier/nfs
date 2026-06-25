using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for PATHCONF (<c>PATHCONF3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3PathConfArgs
{
    /// <summary>The handle of the object to query.</summary>
    [XdrField(0)]
    public Nfs3Handle Handle { get; set; }
}

/// <summary>The result of PATHCONF (<c>PATHCONF3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3PathConfResult : IXdrSerializable<Nfs3PathConfResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The object's attributes, if available.</summary>
    public Nfs3FileAttributes? Attributes { get; set; }

    /// <summary>The maximum number of hard links to an object.</summary>
    public uint LinkMax { get; set; }

    /// <summary>The maximum length of a component of a file name.</summary>
    public uint NameMax { get; set; }

    /// <summary>Whether the server rejects names longer than <see cref="NameMax"/> rather than truncating.</summary>
    public bool NoTruncate { get; set; }

    /// <summary>Whether changing ownership is restricted to privileged users.</summary>
    public bool ChownRestricted { get; set; }

    /// <summary>Whether names differing only in case are treated as the same name.</summary>
    public bool CaseInsensitive { get; set; }

    /// <summary>Whether the server preserves the case of names.</summary>
    public bool CasePreserving { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3PathConfResult Failure(NfsStatus status) => new() { Status = status };

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
            writer.WriteUInt32(LinkMax);
            writer.WriteUInt32(NameMax);
            writer.WriteBool(NoTruncate);
            writer.WriteBool(ChownRestricted);
            writer.WriteBool(CaseInsensitive);
            writer.WriteBool(CasePreserving);
        }
    }

    /// <inheritdoc/>
    public static Nfs3PathConfResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        Nfs3FileAttributes? attributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
        var result = new Nfs3PathConfResult { Status = status, Attributes = attributes };
        if (status == NfsStatus.Ok)
        {
            result.LinkMax = reader.ReadUInt32();
            result.NameMax = reader.ReadUInt32();
            result.NoTruncate = reader.ReadBool();
            result.ChownRestricted = reader.ReadBool();
            result.CaseInsensitive = reader.ReadBool();
            result.CasePreserving = reader.ReadBool();
        }

        return result;
    }
}
