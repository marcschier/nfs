using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>The description of a conflicting lock returned by LOCK/LOCKT (<c>LOCK4denied</c>, RFC 7530).</summary>
public record struct Nfs4LockDenied : IXdrSerializable<Nfs4LockDenied>
{
    /// <summary>The byte offset of the conflicting lock.</summary>
    public ulong Offset { get; set; }

    /// <summary>The length of the conflicting lock.</summary>
    public ulong Length { get; set; }

    /// <summary>The type of the conflicting lock.</summary>
    public Nfs4LockType LockType { get; set; }

    /// <summary>The owner of the conflicting lock.</summary>
    public Nfs4LockOwner Owner { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteUInt32((uint)LockType);
        writer.WriteUInt64(Owner.ClientId);
        writer.WriteOpaqueVariable(Owner.Owner ?? []);
    }

    /// <inheritdoc/>
    public static Nfs4LockDenied ReadFrom(ref XdrReader reader)
    {
        ulong offset = reader.ReadUInt64();
        ulong length = reader.ReadUInt64();
        var lockType = (Nfs4LockType)reader.ReadUInt32();
        ulong clientId = reader.ReadUInt64();
        byte[] owner = reader.ReadOpaqueVariable(1024).ToArray();
        return new Nfs4LockDenied
        {
            Offset = offset,
            Length = length,
            LockType = lockType,
            Owner = new Nfs4LockOwner(clientId, owner),
        };
    }
}

/// <summary>The result of LOCK (the lock state identifier on success, conflict details when denied).</summary>
public sealed class Nfs4LockResult : Nfs4ResOp
{
    /// <summary>Gets or sets the lock state identifier (valid when the lock was granted).</summary>
    public Nfs4StateId StateId { get; set; }

    /// <summary>Gets or sets the conflicting lock (valid when <see cref="Nfs4ResOp.Status"/> is DENIED).</summary>
    public Nfs4LockDenied Denied { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Lock;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == Nfs4Status.Ok)
        {
            StateId.WriteTo(ref writer);
        }
        else if (Status == Nfs4Status.LockDenied)
        {
            Denied.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (Status == Nfs4Status.Ok)
        {
            StateId = Nfs4StateId.ReadFrom(ref reader);
        }
        else if (Status == Nfs4Status.LockDenied)
        {
            Denied = Nfs4LockDenied.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of LOCKT (conflict details when denied; otherwise just a status).</summary>
public sealed class Nfs4LockTestResult : Nfs4ResOp
{
    /// <summary>Gets or sets the conflicting lock (valid when <see cref="Nfs4ResOp.Status"/> is DENIED).</summary>
    public Nfs4LockDenied Denied { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LockTest;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status == Nfs4Status.LockDenied)
        {
            Denied.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (Status == Nfs4Status.LockDenied)
        {
            Denied = Nfs4LockDenied.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of LOCKU (the updated lock state identifier on success).</summary>
public sealed class Nfs4LockUnlockResult : Nfs4ResOp
{
    /// <summary>Gets or sets the updated lock state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LockUnlock;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            StateId.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            StateId = Nfs4StateId.ReadFrom(ref reader);
        }
    }
}
