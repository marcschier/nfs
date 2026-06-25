using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for WRITE (<c>WRITE3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3WriteArgs
{
    /// <summary>The file handle.</summary>
    [XdrField(0)]
    public Nfs3Handle File { get; set; }

    /// <summary>The byte offset to write at.</summary>
    [XdrField(1)]
    public ulong Offset { get; set; }

    /// <summary>The number of bytes to write.</summary>
    [XdrField(2)]
    public uint Count { get; set; }

    /// <summary>How durably the server must commit the data.</summary>
    [XdrField(3)]
    public Nfs3StableHow Stable { get; set; }

    /// <summary>The data to write.</summary>
    [XdrField(4)]
    [XdrOpaque(Nfs3.MaxWriteSize)]
    public byte[] Data { get; set; }
}

/// <summary>The success arm of WRITE (<c>WRITE3resok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3WriteResultOk
{
    /// <summary>Weak cache-consistency data for the file.</summary>
    [XdrField(0)]
    public Nfs3WccData FileWcc { get; set; }

    /// <summary>The number of bytes written.</summary>
    [XdrField(1)]
    public uint Count { get; set; }

    /// <summary>How durably the server actually committed the data.</summary>
    [XdrField(2)]
    public Nfs3StableHow Committed { get; set; }

    /// <summary>The server's write verifier, used to detect a server restart.</summary>
    [XdrField(3)]
    [XdrFixedOpaque(8)]
    public byte[] Verifier { get; set; }
}

/// <summary>The failure arm of WRITE (<c>WRITE3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3WriteResultFail
{
    /// <summary>Weak cache-consistency data for the file.</summary>
    [XdrField(0)]
    public Nfs3WccData FileWcc { get; set; }
}

/// <summary>The result of WRITE (<c>WRITE3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3WriteResult : IXdrSerializable<Nfs3WriteResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3WriteResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3WriteResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the write succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3WriteResult Success(Nfs3WriteResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3WriteResult Failure(NfsStatus status, Nfs3WriteResultFail fail = default) =>
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
    public static Nfs3WriteResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3WriteResult { Status = status, Ok = Nfs3WriteResultOk.ReadFrom(ref reader) }
            : new Nfs3WriteResult { Status = status, Fail = Nfs3WriteResultFail.ReadFrom(ref reader) };
    }
}
