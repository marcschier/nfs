using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for LINK (<c>LINK3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3LinkArgs
{
    /// <summary>The handle of the existing object to link to.</summary>
    [XdrField(0)]
    public Nfs3Handle File { get; set; }

    /// <summary>The directory and name of the new link.</summary>
    [XdrField(1)]
    public Nfs3DirOpArgs Link { get; set; }
}

/// <summary>
/// The result of LINK (<c>LINK3res</c>, RFC 1813). Both arms carry the target's post-operation
/// attributes and weak cache-consistency data for the link's directory.
/// </summary>
public record struct Nfs3LinkResult : IXdrSerializable<Nfs3LinkResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The target's attributes, if available.</summary>
    public Nfs3FileAttributes? FileAttributes { get; set; }

    /// <summary>Weak cache-consistency data for the directory holding the new link.</summary>
    public Nfs3WccData LinkDirectoryWcc { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="fileAttributes">The target's attributes.</param>
    /// <returns>The result.</returns>
    public static Nfs3LinkResult Success(Nfs3FileAttributes? fileAttributes = null) =>
        new() { Status = NfsStatus.Ok, FileAttributes = fileAttributes };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3LinkResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (FileAttributes.HasValue)
        {
            writer.WriteBool(true);
            FileAttributes.Value.WriteTo(ref writer);
        }
        else
        {
            writer.WriteBool(false);
        }

        LinkDirectoryWcc.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    public static Nfs3LinkResult ReadFrom(ref XdrReader reader) => new()
    {
        Status = (NfsStatus)reader.ReadInt32(),
        FileAttributes = reader.ReadBool() ? Nfs3FileAttributes.ReadFrom(ref reader) : null,
        LinkDirectoryWcc = Nfs3WccData.ReadFrom(ref reader),
    };
}
