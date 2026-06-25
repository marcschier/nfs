using Nfs.Xdr;

namespace Nfs.Nlm;

/// <summary>A lock as described to the lock manager (<c>nlm4_lock</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4Lock
{
    /// <summary>The name of the host making the request.</summary>
    [XdrField(0)]
    [XdrString(Nlm4.MaxStringLength)]
    public string CallerName { get; set; }

    /// <summary>The file handle of the object to lock (an opaque network object).</summary>
    [XdrField(1)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] FileHandle { get; set; }

    /// <summary>The lock owner handle (an opaque network object).</summary>
    [XdrField(2)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Owner { get; set; }

    /// <summary>The owning process identifier.</summary>
    [XdrField(3)]
    public int ServerId { get; set; }

    /// <summary>The byte offset of the lock.</summary>
    [XdrField(4)]
    public ulong Offset { get; set; }

    /// <summary>The length of the lock (0 means to end of file).</summary>
    [XdrField(5)]
    public ulong Length { get; set; }
}

/// <summary>The holder of a conflicting lock (<c>nlm4_holder</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4Holder
{
    /// <summary>Whether the conflicting lock is exclusive.</summary>
    [XdrField(0)]
    public bool Exclusive { get; set; }

    /// <summary>The conflicting owner's process identifier.</summary>
    [XdrField(1)]
    public int ServerId { get; set; }

    /// <summary>The conflicting owner handle.</summary>
    [XdrField(2)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Owner { get; set; }

    /// <summary>The byte offset of the conflicting lock.</summary>
    [XdrField(3)]
    public ulong Offset { get; set; }

    /// <summary>The length of the conflicting lock.</summary>
    [XdrField(4)]
    public ulong Length { get; set; }
}

/// <summary>Arguments for NLM_TEST (<c>nlm4_testargs</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4TestArgs
{
    /// <summary>An opaque cookie echoed back in the reply.</summary>
    [XdrField(0)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Cookie { get; set; }

    /// <summary>Whether an exclusive lock is being tested.</summary>
    [XdrField(1)]
    public bool Exclusive { get; set; }

    /// <summary>The lock to test.</summary>
    [XdrField(2)]
    public Nlm4Lock Lock { get; set; }
}

/// <summary>Arguments for NLM_LOCK (<c>nlm4_lockargs</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4LockArgs
{
    /// <summary>An opaque cookie echoed back in the reply.</summary>
    [XdrField(0)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Cookie { get; set; }

    /// <summary>Whether the request should block until the lock can be granted.</summary>
    [XdrField(1)]
    public bool Block { get; set; }

    /// <summary>Whether an exclusive lock is requested.</summary>
    [XdrField(2)]
    public bool Exclusive { get; set; }

    /// <summary>The lock to acquire.</summary>
    [XdrField(3)]
    public Nlm4Lock Lock { get; set; }

    /// <summary>Whether the lock is being reclaimed after a server reboot.</summary>
    [XdrField(4)]
    public bool Reclaim { get; set; }

    /// <summary>The client's NSM state number.</summary>
    [XdrField(5)]
    public int State { get; set; }
}

/// <summary>Arguments for NLM_UNLOCK (<c>nlm4_unlockargs</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4UnlockArgs
{
    /// <summary>An opaque cookie echoed back in the reply.</summary>
    [XdrField(0)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Cookie { get; set; }

    /// <summary>The lock to release.</summary>
    [XdrField(1)]
    public Nlm4Lock Lock { get; set; }
}

/// <summary>Arguments for NLM_CANCEL (<c>nlm4_cancargs</c>, X/Open).</summary>
[XdrType]
public partial struct Nlm4CancelArgs
{
    /// <summary>An opaque cookie echoed back in the reply.</summary>
    [XdrField(0)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Cookie { get; set; }

    /// <summary>Whether the cancelled request was blocking.</summary>
    [XdrField(1)]
    public bool Block { get; set; }

    /// <summary>Whether the cancelled request was exclusive.</summary>
    [XdrField(2)]
    public bool Exclusive { get; set; }

    /// <summary>The lock whose request is cancelled.</summary>
    [XdrField(3)]
    public Nlm4Lock Lock { get; set; }
}

/// <summary>A simple status reply (<c>nlm4_res</c>, X/Open). Used by LOCK, UNLOCK, CANCEL, and GRANTED.</summary>
[XdrType]
public partial struct Nlm4Res
{
    /// <summary>The cookie echoed from the request.</summary>
    [XdrField(0)]
    [XdrOpaque(Nlm4.MaxNetObject)]
    public byte[] Cookie { get; set; }

    /// <summary>The operation status.</summary>
    [XdrField(1)]
    public Nlm4Status Status { get; set; }
}

/// <summary>The reply to NLM_TEST (<c>nlm4_testres</c>, X/Open).</summary>
public record struct Nlm4TestRes : IXdrSerializable<Nlm4TestRes>
{
    /// <summary>The cookie echoed from the request.</summary>
    public byte[] Cookie { get; set; }

    /// <summary>The operation status.</summary>
    public Nlm4Status Status { get; set; }

    /// <summary>The conflicting lock's holder (present only when <see cref="Status"/> is denied).</summary>
    public Nlm4Holder? Holder { get; set; }

    /// <summary>Gets a value indicating whether the lock would be granted.</summary>
    public readonly bool IsGranted => Status == Nlm4Status.Granted;

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteOpaqueVariable(Cookie ?? []);
        writer.WriteInt32((int)Status);
        if (Status == Nlm4Status.Denied && Holder is { } holder)
        {
            holder.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nlm4TestRes ReadFrom(ref XdrReader reader)
    {
        byte[] cookie = reader.ReadOpaqueVariable(Nlm4.MaxNetObject).ToArray();
        var status = (Nlm4Status)reader.ReadInt32();
        Nlm4Holder? holder = status == Nlm4Status.Denied ? Nlm4Holder.ReadFrom(ref reader) : null;
        return new Nlm4TestRes { Cookie = cookie, Status = status, Holder = holder };
    }
}
