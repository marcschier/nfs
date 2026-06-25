using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>Arguments for READ (<c>READ3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadArgs
{
    /// <summary>The file handle.</summary>
    [XdrField(0)]
    public Nfs3Handle File { get; set; }

    /// <summary>The byte offset to read from.</summary>
    [XdrField(1)]
    public ulong Offset { get; set; }

    /// <summary>The maximum number of bytes to read.</summary>
    [XdrField(2)]
    public uint Count { get; set; }
}

/// <summary>The success arm of READ (<c>READ3resok</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadResultOk
{
    /// <summary>The file's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? FileAttributes { get; set; }

    /// <summary>The number of bytes returned.</summary>
    [XdrField(1)]
    public uint Count { get; set; }

    /// <summary>Whether the end of the file was reached.</summary>
    [XdrField(2)]
    public bool Eof { get; set; }

    /// <summary>The data read.</summary>
    [XdrField(3)]
    [XdrOpaque(Nfs3.MaxReadSize)]
    public byte[] Data { get; set; }
}

/// <summary>The failure arm of READ (<c>READ3resfail</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3ReadResultFail
{
    /// <summary>The file's attributes, if available.</summary>
    [XdrField(0)]
    public Nfs3FileAttributes? FileAttributes { get; set; }
}

/// <summary>The result of READ (<c>READ3res</c>, RFC 1813), discriminated on the status.</summary>
public record struct Nfs3ReadResult : IXdrSerializable<Nfs3ReadResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3ReadResultOk Ok { get; set; }

    /// <summary>The failure data (valid otherwise).</summary>
    public Nfs3ReadResultFail Fail { get; set; }

    /// <summary>Gets a value indicating whether the read succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadResult Success(Nfs3ReadResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="fail">The failure data.</param>
    /// <returns>The result.</returns>
    public static Nfs3ReadResult Failure(NfsStatus status, Nfs3ReadResultFail fail = default) =>
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
    public static Nfs3ReadResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3ReadResult { Status = status, Ok = Nfs3ReadResultOk.ReadFrom(ref reader) }
            : new Nfs3ReadResult { Status = status, Fail = Nfs3ReadResultFail.ReadFrom(ref reader) };
    }
}
