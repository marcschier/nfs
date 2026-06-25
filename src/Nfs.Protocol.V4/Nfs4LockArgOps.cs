using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>The kind of a byte-range lock (<c>nfs_lock_type4</c>, RFC 7530).</summary>
public enum Nfs4LockType
{
    /// <summary>A non-blocking shared (read) lock (READ_LT).</summary>
    Read = 1,

    /// <summary>A non-blocking exclusive (write) lock (WRITE_LT).</summary>
    Write = 2,

    /// <summary>A blocking shared (read) lock (READW_LT).</summary>
    ReadBlocking = 3,

    /// <summary>A blocking exclusive (write) lock (WRITEW_LT).</summary>
    WriteBlocking = 4,
}

/// <summary>The owner of a byte-range lock (<c>lock_owner4</c>, RFC 7530).</summary>
/// <param name="ClientId">The owning client identifier.</param>
/// <param name="Owner">The opaque lock-owner.</param>
public readonly record struct Nfs4LockOwner(ulong ClientId, byte[] Owner);

/// <summary>LOCK: acquire a byte-range lock.</summary>
public sealed class Nfs4LockOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the lock type.</summary>
    public Nfs4LockType LockType { get; set; }

    /// <summary>Gets or sets whether the lock is being reclaimed after a server reboot.</summary>
    public bool Reclaim { get; set; }

    /// <summary>Gets or sets the byte offset of the lock.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the length of the lock (<c>0xFFFFFFFFFFFFFFFF</c> means to end of file).</summary>
    public ulong Length { get; set; }

    /// <summary>Gets or sets whether a new lock-owner is being established from an open.</summary>
    public bool NewLockOwner { get; set; }

    /// <summary>Gets or sets the open-owner sequence (when <see cref="NewLockOwner"/> is true).</summary>
    public uint OpenSeqid { get; set; }

    /// <summary>Gets or sets the open state identifier (when <see cref="NewLockOwner"/> is true).</summary>
    public Nfs4StateId OpenStateId { get; set; }

    /// <summary>Gets or sets the lock-owner sequence value.</summary>
    public uint LockSeqid { get; set; }

    /// <summary>Gets or sets the lock-owner (when <see cref="NewLockOwner"/> is true).</summary>
    public Nfs4LockOwner LockOwner { get; set; }

    /// <summary>Gets or sets the existing lock state identifier (when <see cref="NewLockOwner"/> is false).</summary>
    public Nfs4StateId LockStateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Lock;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)LockType);
        writer.WriteBool(Reclaim);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteBool(NewLockOwner);
        if (NewLockOwner)
        {
            writer.WriteUInt32(OpenSeqid);
            OpenStateId.WriteTo(ref writer);
            writer.WriteUInt32(LockSeqid);
            writer.WriteUInt64(LockOwner.ClientId);
            writer.WriteOpaqueVariable(LockOwner.Owner ?? []);
        }
        else
        {
            LockStateId.WriteTo(ref writer);
            writer.WriteUInt32(LockSeqid);
        }
    }
}

/// <summary>LOCKT: test whether a byte-range lock could be acquired.</summary>
public sealed class Nfs4LockTestOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the lock type to test.</summary>
    public Nfs4LockType LockType { get; set; }

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the length (<c>0xFFFFFFFFFFFFFFFF</c> means to end of file).</summary>
    public ulong Length { get; set; }

    /// <summary>Gets or sets the prospective lock-owner.</summary>
    public Nfs4LockOwner Owner { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LockTest;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)LockType);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteUInt64(Owner.ClientId);
        writer.WriteOpaqueVariable(Owner.Owner ?? []);
    }
}

/// <summary>LOCKU: release a byte-range lock.</summary>
public sealed class Nfs4LockUnlockOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the lock type being released.</summary>
    public Nfs4LockType LockType { get; set; }

    /// <summary>Gets or sets the lock-owner sequence value.</summary>
    public uint Seqid { get; set; }

    /// <summary>Gets or sets the lock state identifier.</summary>
    public Nfs4StateId LockStateId { get; set; }

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the length.</summary>
    public ulong Length { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LockUnlock;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)LockType);
        writer.WriteUInt32(Seqid);
        LockStateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
    }
}
