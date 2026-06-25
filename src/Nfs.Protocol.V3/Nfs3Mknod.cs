using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Device data for MKNOD character and block devices (<c>devicedata3</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3DeviceData
{
    /// <summary>The initial attributes for the special node.</summary>
    [XdrField(0)]
    public Nfs3SetAttributes Attributes { get; set; }

    /// <summary>The major/minor device numbers.</summary>
    [XdrField(1)]
    public Nfs3SpecData Spec { get; set; }
}

/// <summary>Discriminated MKNOD data (<c>mknoddata3</c>, RFC 1813).</summary>
public record struct Nfs3MknodData : IXdrSerializable<Nfs3MknodData>
{
    /// <summary>The node type to create.</summary>
    public NfsFileType Type { get; set; }

    /// <summary>The initial attributes for FIFO and socket nodes.</summary>
    public Nfs3SetAttributes Attributes { get; set; }

    /// <summary>The device data for character and block devices.</summary>
    public Nfs3DeviceData Device { get; set; }

    /// <summary>Creates FIFO MKNOD data.</summary>
    /// <param name="attributes">The initial attributes.</param>
    /// <returns>The MKNOD data.</returns>
    public static Nfs3MknodData Fifo(Nfs3SetAttributes attributes = default) =>
        new() { Type = NfsFileType.Fifo, Attributes = attributes };

    /// <summary>Creates socket MKNOD data.</summary>
    /// <param name="attributes">The initial attributes.</param>
    /// <returns>The MKNOD data.</returns>
    public static Nfs3MknodData Socket(Nfs3SetAttributes attributes = default) =>
        new() { Type = NfsFileType.Socket, Attributes = attributes };

    /// <summary>Creates block-device MKNOD data.</summary>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="spec">The major/minor device numbers.</param>
    /// <returns>The MKNOD data.</returns>
    public static Nfs3MknodData BlockDevice(Nfs3SetAttributes attributes = default, Nfs3SpecData spec = default) =>
        new() { Type = NfsFileType.BlockDevice, Device = new Nfs3DeviceData { Attributes = attributes, Spec = spec } };

    /// <summary>Creates character-device MKNOD data.</summary>
    /// <param name="attributes">The initial attributes.</param>
    /// <param name="spec">The major/minor device numbers.</param>
    /// <returns>The MKNOD data.</returns>
    public static Nfs3MknodData CharacterDevice(Nfs3SetAttributes attributes = default, Nfs3SpecData spec = default) =>
        new() { Type = NfsFileType.CharacterDevice, Device = new Nfs3DeviceData { Attributes = attributes, Spec = spec } };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Type);
        switch (Type)
        {
            case NfsFileType.Fifo:
            case NfsFileType.Socket:
                Attributes.WriteTo(ref writer);
                break;
            case NfsFileType.CharacterDevice:
            case NfsFileType.BlockDevice:
                Device.WriteTo(ref writer);
                break;
        }
    }

    /// <inheritdoc/>
    public static Nfs3MknodData ReadFrom(ref XdrReader reader)
    {
        var type = (NfsFileType)reader.ReadInt32();
        return type switch
        {
            NfsFileType.Fifo or NfsFileType.Socket => new Nfs3MknodData
            {
                Type = type,
                Attributes = Nfs3SetAttributes.ReadFrom(ref reader),
            },
            NfsFileType.CharacterDevice or NfsFileType.BlockDevice => new Nfs3MknodData
            {
                Type = type,
                Device = Nfs3DeviceData.ReadFrom(ref reader),
            },
            _ => new Nfs3MknodData { Type = type },
        };
    }
}

/// <summary>Arguments for MKNOD (<c>MKNOD3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3MknodArgs
{
    /// <summary>The parent directory and new node name.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs Where { get; set; }

    /// <summary>The node type-specific data.</summary>
    [XdrField(1)]
    public Nfs3MknodData Data { get; set; }
}
