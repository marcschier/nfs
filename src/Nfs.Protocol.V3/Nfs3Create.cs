using Nfs.Abstractions;
using Nfs.Xdr;

namespace Nfs.Protocol.V3;

/// <summary>How a CREATE handles an existing file (<c>createmode3</c>, RFC 1813).</summary>
public enum Nfs3CreateMode
{
    /// <summary>Create the file, overwriting any existing one (UNCHECKED).</summary>
    Unchecked = 0,

    /// <summary>Fail if the file already exists (GUARDED).</summary>
    Guarded = 1,

    /// <summary>Exclusive create using a verifier for idempotency (EXCLUSIVE).</summary>
    Exclusive = 2,
}

/// <summary>How a file is to be created (<c>createhow3</c>, RFC 1813).</summary>
public record struct Nfs3CreateHow : IXdrSerializable<Nfs3CreateHow>
{
    /// <summary>The create mode.</summary>
    public Nfs3CreateMode Mode { get; set; }

    /// <summary>The initial attributes (used for <see cref="Nfs3CreateMode.Unchecked"/>/<see cref="Nfs3CreateMode.Guarded"/>).</summary>
    public Nfs3SetAttributes Attributes { get; set; }

    /// <summary>The 8-byte exclusive-create verifier (used for <see cref="Nfs3CreateMode.Exclusive"/>).</summary>
    public byte[]? Verifier { get; set; }

    /// <summary>Creates an UNCHECKED createhow with the given initial attributes.</summary>
    /// <param name="attributes">The initial attributes.</param>
    /// <returns>The createhow.</returns>
    public static Nfs3CreateHow CreateUnchecked(Nfs3SetAttributes attributes) =>
        new() { Mode = Nfs3CreateMode.Unchecked, Attributes = attributes };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Mode);
        if (Mode == Nfs3CreateMode.Exclusive)
        {
            writer.WriteOpaqueFixed(Verifier ?? new byte[8]);
        }
        else
        {
            Attributes.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3CreateHow ReadFrom(ref XdrReader reader)
    {
        var mode = (Nfs3CreateMode)reader.ReadInt32();
        return mode == Nfs3CreateMode.Exclusive
            ? new Nfs3CreateHow { Mode = mode, Verifier = reader.ReadOpaqueFixed(8).ToArray() }
            : new Nfs3CreateHow { Mode = mode, Attributes = Nfs3SetAttributes.ReadFrom(ref reader) };
    }
}

/// <summary>Arguments for CREATE (<c>CREATE3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3CreateArgs
{
    /// <summary>The directory and name of the new file.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs Where { get; set; }

    /// <summary>How to create the file.</summary>
    [XdrField(1)]
    public Nfs3CreateHow How { get; set; }
}

/// <summary>Arguments for MKDIR (<c>MKDIR3args</c>, RFC 1813).</summary>
[XdrType]
public partial struct Nfs3MkdirArgs
{
    /// <summary>The parent directory and name of the new directory.</summary>
    [XdrField(0)]
    public Nfs3DirOpArgs Where { get; set; }

    /// <summary>The initial attributes for the new directory.</summary>
    [XdrField(1)]
    public Nfs3SetAttributes Attributes { get; set; }
}

/// <summary>The success arm shared by CREATE and MKDIR (RFC 1813).</summary>
[XdrType]
public partial struct Nfs3CreateResultOk
{
    /// <summary>The new object's handle, if the server returned one.</summary>
    [XdrField(0)]
    public Nfs3Handle? Handle { get; set; }

    /// <summary>The new object's attributes, if available.</summary>
    [XdrField(1)]
    public Nfs3FileAttributes? Attributes { get; set; }

    /// <summary>Weak cache-consistency data for the parent directory.</summary>
    [XdrField(2)]
    public Nfs3WccData DirectoryWcc { get; set; }
}

/// <summary>The result of CREATE or MKDIR, discriminated on the status (RFC 1813).</summary>
public record struct Nfs3CreateResult : IXdrSerializable<Nfs3CreateResult>
{
    /// <summary>The operation status.</summary>
    public NfsStatus Status { get; set; }

    /// <summary>The success data (valid when <see cref="Status"/> is <see cref="NfsStatus.Ok"/>).</summary>
    public Nfs3CreateResultOk Ok { get; set; }

    /// <summary>The parent directory's weak cache-consistency data on failure.</summary>
    public Nfs3WccData FailureWcc { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public readonly bool IsSuccess => Status == NfsStatus.Ok;

    /// <summary>Creates a successful result.</summary>
    /// <param name="ok">The success data.</param>
    /// <returns>The result.</returns>
    public static Nfs3CreateResult Success(Nfs3CreateResultOk ok) => new() { Status = NfsStatus.Ok, Ok = ok };

    /// <summary>Creates a failed result.</summary>
    /// <param name="status">The failure status.</param>
    /// <param name="failureWcc">The parent directory's weak cache-consistency data.</param>
    /// <returns>The result.</returns>
    public static Nfs3CreateResult Failure(NfsStatus status, Nfs3WccData failureWcc = default) =>
        new() { Status = status, FailureWcc = failureWcc };

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
            FailureWcc.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs3CreateResult ReadFrom(ref XdrReader reader)
    {
        var status = (NfsStatus)reader.ReadInt32();
        return status == NfsStatus.Ok
            ? new Nfs3CreateResult { Status = status, Ok = Nfs3CreateResultOk.ReadFrom(ref reader) }
            : new Nfs3CreateResult { Status = status, FailureWcc = Nfs3WccData.ReadFrom(ref reader) };
    }
}
