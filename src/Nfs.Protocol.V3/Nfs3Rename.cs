using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for RENAME (<c>RENAME3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3RenameArgs
{
    /// <summary>The directory and name of the object to move.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs From { get; set; }

    /// <summary>The destination directory and new name.</summary>
    [XdrField(1)]
    public Nfs3DirOpArgs To { get; set; }
}

/// <summary>
/// The result of RENAME (<c>RENAME3res</c>, RFC 1813). Both arms carry weak cache-consistency data
/// for the source and destination directories.
/// </summary>
public record struct Nfs3RenameResult : IXdrSerializable<Nfs3RenameResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The source directory's weak cache-consistency data.</summary>
    public Nfs3WccData FromDirectoryWcc { get; set; }

    /// <summary>The destination directory's weak cache-consistency data.</summary>
    public Nfs3WccData ToDirectoryWcc { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <returns>The result.</returns>
    public static Nfs3RenameResult Success() => new() { Status = NfsStatus.Ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3RenameResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        FromDirectoryWcc.WriteTo(ref writer);
        ToDirectoryWcc.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    public static Nfs3RenameResult ReadFrom(ref XdrReader reader) => new()
    {
        Status = (NfsStatus)reader.ReadInt32(),
        FromDirectoryWcc = Nfs3WccData.ReadFrom(ref reader),
        ToDirectoryWcc = Nfs3WccData.ReadFrom(ref reader),
    };
}
