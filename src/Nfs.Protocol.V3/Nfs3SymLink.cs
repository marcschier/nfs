using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for READLINK (<c>READLINK3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadLinkArgs
{
    /// <summary>The handle of the symbolic link to read.</summary>
    [XdrField(0)]
    public Nfs3Handle SymbolicLink { get; set; }
}

/// <summary>The result of READLINK (<c>READLINK3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3ReadLinkResult : IXdrSerializable<Nfs3ReadLinkResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The link's attributes, if available.</summary>
    public Nfs3FileAttributes? Attributes { get; set; }

    /// <summary>The link target path (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public string? Target { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="target">The link target path.</param>
    /// <param name="attributes">The link's attributes.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadLinkResult Success(string target, Nfs3FileAttributes? attributes = null) =>
        new() { Status = NfsStatus.Ok, Target = target, Attributes = attributes };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadLinkResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        WriteOptionalAttributes(ref writer, Attributes);
        if (Status == NfsStatus.Ok)
        {
            writer.WriteString(Target ?? string.Empty);
        }
    }

    /// <inheritdoc/>
    public static Nfs3ReadLinkResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        Nfs3FileAttributes? attributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null;
        return status == NfsStatus.Ok
            ? new Nfs3ReadLinkResult { Status = status, Attributes = attributes, Target = reader.ReadString(Nfs3.MaxPathLength) }
            : new Nfs3ReadLinkResult { Status = status, Attributes = attributes };
    }

    private static void WriteOptionalAttributes(ref XdrWriter writer, Nfs3FileAttributes? attributes)
    {
        if (attributes.HasValue)
        {
            writer.WriteBool(true);
            attributes.Value.WriteTo(ref writer);
        }
        else
        {
            writer.WriteBool(false);
        }
    }
}

/// <summary>Arguments for SYMLINK (<c>SYMLINK3args</c>, RFC 1813).</summary>
public record struct Nfs3SymlinkArgs : IXdrSerializable<Nfs3SymlinkArgs>
{
    /// <summary>The parent directory and name of the new link.</summary>
    public Nfs3DirOpArgs Where { get; set; }

    /// <summary>The initial attributes for the new link.</summary>
    public Nfs3SetAttributes Attributes { get; set; }

    /// <summary>The path the link points to.</summary>
    public string Target { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        Where.WriteTo(ref writer);
        Attributes.WriteTo(ref writer);
        writer.WriteString(Target ?? string.Empty);
    }

    /// <inheritdoc/>
    public static Nfs3SymlinkArgs ReadFrom(ref XdrReader reader) => new()
    {
        Where = Nfs3DirOpArgs.ReadFrom(ref reader),
        Attributes = Nfs3SetAttributes.ReadFrom(ref reader),
        Target = reader.ReadString(Nfs3.MaxPathLength),
    };
}
