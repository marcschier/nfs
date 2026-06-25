using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for COMMIT (<c>COMMIT3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3CommitArgs
{
    /// <summary>The file handle.</summary>
    [XdrField(0)]
    public Nfs3Handle File { get; set; }

    /// <summary>The byte offset at which the flush begins (0 means the whole file).</summary>
    [XdrField(1)]
    public ulong Offset { get; set; }

    /// <summary>The number of bytes to flush (0 means to the end of the file).</summary>
    [XdrField(2)]
    public uint Count { get; set; }
}

/// <summary>The result of COMMIT (<c>COMMIT3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3CommitResult : IXdrSerializable<Nfs3CommitResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>Weak cache-consistency data for the file.</summary>
    public Nfs3WccData FileWcc { get; set; }

    /// <summary>The server's write verifier, used to detect a server restart (8 bytes).</summary>
    public byte[]? Verifier { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="verifier">The server's write verifier.</param>
    /// <returns>The result.</returns>
    public static Nfs3CommitResult Success(byte[] verifier) =>
        new() { Status = NfsStatus.Ok, Verifier = verifier };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <returns>The result.</returns>
    public static Nfs3CommitResult Failure(NfsStatus status) => new() { Status = status };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        FileWcc.WriteTo(ref writer);
        if (Status == NfsStatus.Ok)
        {
            writer.WriteOpaqueFixed(Verifier ?? new byte[8]);
        }
    }

    /// <inheritdoc/>
    public static Nfs3CommitResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        var result = new Nfs3CommitResult { Status = status, FileWcc = Nfs3WccData.ReadFrom(ref reader) };
        if (status == NfsStatus.Ok)
        {
            result.Verifier = reader.ReadOpaqueFixed(8).ToArray();
        }

        return result;
    }
}
