using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>
/// An optional guard for SETATTR that makes the operation conditional on the object's
/// change-time (<c>sattrguard3</c>, RFC 1813).
/// </summary>
public record struct Nfs3SetAttrGuard : IXdrSerializable<Nfs3SetAttrGuard>
{
    /// <summary>The change-time the object must currently have, or <see langword="null"/> for no guard.</summary>
    public Nfs3Time? ObjectChangeTime { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        if (ObjectChangeTime is { } time)
        {
            writer.WriteBool(true);
            time.WriteTo(ref writer);
        }
        else
        {
            writer.WriteBool(false);
        }
    }

    /// <inheritdoc/>
    public static Nfs3SetAttrGuard ReadFrom(ref XdrReader reader) =>
        reader.ReadBool()
            ? new Nfs3SetAttrGuard { ObjectChangeTime = Nfs3Time.ReadFrom(ref reader) }
            : default;
}

/// <summary>Arguments for SETATTR (<c>SETATTR3args</c>, RFC 1813).</summary>
public record struct Nfs3SetAttrArgs : IXdrSerializable<Nfs3SetAttrArgs>
{
    /// <summary>The handle of the object to modify.</summary>
    public Nfs3Handle Handle { get; set; }

    /// <summary>The attributes to set.</summary>
    public Nfs3SetAttributes Attributes { get; set; }

    /// <summary>An optional change-time guard.</summary>
    public Nfs3SetAttrGuard Guard { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        Handle.WriteTo(ref writer);
        Attributes.WriteTo(ref writer);
        Guard.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    public static Nfs3SetAttrArgs ReadFrom(ref XdrReader reader) => new()
    {
        Handle = Nfs3Handle.ReadFrom(ref reader),
        Attributes = Nfs3SetAttributes.ReadFrom(ref reader),
        Guard = Nfs3SetAttrGuard.ReadFrom(ref reader),
    };
}
