using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for REMOVE and RMDIR (<c>REMOVE3args</c>/<c>RMDIR3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3RemoveArgs
{
    /// <summary>The directory and name of the object to remove.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs Target { get; set; }
}

/// <summary>
/// A result that carries only weak cache-consistency data, used by REMOVE and RMDIR
/// (both the success and failure arms are <c>wcc_data</c>, RFC 1813).
/// </summary>
public record struct Nfs3WccResult : IXdrSerializable<Nfs3WccResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The parent directory's weak cache-consistency data.</summary>
    public Nfs3WccData DirectoryWcc { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="directoryWcc">The parent directory's weak cache-consistency data.</param>
    /// <returns>The result.</returns>
    public static Nfs3WccResult Success(Nfs3WccData directoryWcc = default) =>
        new() { Status = NfsStatus.Ok, DirectoryWcc = directoryWcc };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="directoryWcc">The parent directory's weak cache-consistency data.</param>
    /// <returns>The result.</returns>
    public static Nfs3WccResult Failure(NfsStatus status, Nfs3WccData directoryWcc = default) =>
        new() { Status = status, DirectoryWcc = directoryWcc };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        DirectoryWcc.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    public static Nfs3WccResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return new Nfs3WccResult { Status = status, DirectoryWcc = Nfs3WccData.ReadFrom(ref reader) };
    }
}
