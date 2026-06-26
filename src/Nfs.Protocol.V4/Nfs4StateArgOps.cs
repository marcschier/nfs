using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>Share-access bits for OPEN (RFC 7530 §18.16).</summary>
public static class Nfs4ShareAccess
{
    /// <summary>Open for reading (OPEN4_SHARE_ACCESS_READ).</summary>
    public const uint Read = 1;

    /// <summary>Open for writing (OPEN4_SHARE_ACCESS_WRITE).</summary>
    public const uint Write = 2;

    /// <summary>Open for reading and writing (OPEN4_SHARE_ACCESS_BOTH).</summary>
    public const uint Both = 3;
}

/// <summary>How an OPEN treats the named object (<c>opentype4</c>, RFC 7530).</summary>
public enum Nfs4OpenType
{
    /// <summary>Open an existing object (OPEN4_NOCREATE).</summary>
    NoCreate = 0,

    /// <summary>Create the object if it does not exist (OPEN4_CREATE).</summary>
    Create = 1,
}

/// <summary>How an OPEN with create semantics handles an existing object (<c>createmode4</c>, RFC 7530).</summary>
public enum Nfs4CreateMode
{
    /// <summary>Create or truncate, applying the supplied attributes (UNCHECKED4).</summary>
    Unchecked = 0,

    /// <summary>Fail if the object already exists (GUARDED4).</summary>
    Guarded = 1,

    /// <summary>Exclusive create keyed by a verifier (EXCLUSIVE4).</summary>
    Exclusive = 2,
}

/// <summary>SETCLIENTID: establish a client identifier with the server.</summary>
public sealed class Nfs4SetClientIdOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the client-supplied verifier (8 bytes).</summary>
    public byte[] Verifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Gets or sets the client-chosen opaque identifier.</summary>
    public byte[] Id { get; set; } = [];

    /// <summary>Gets or sets the callback program number (0 if no callback).</summary>
    public uint CallbackProgram { get; set; }

    /// <summary>Gets or sets the callback network identifier (e.g. "tcp").</summary>
    public string CallbackNetId { get; set; } = string.Empty;

    /// <summary>Gets or sets the callback universal address.</summary>
    public string CallbackAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the callback identifier.</summary>
    public uint CallbackIdent { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetClientId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(Verifier);
        writer.WriteOpaqueVariable(Id);
        writer.WriteUInt32(CallbackProgram);
        writer.WriteString(CallbackNetId);
        writer.WriteString(CallbackAddress);
        writer.WriteUInt32(CallbackIdent);
    }
}

/// <summary>SETCLIENTID_CONFIRM: confirm a client identifier.</summary>
public sealed class Nfs4SetClientIdConfirmOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the client identifier returned by SETCLIENTID.</summary>
    public ulong ClientId { get; set; }

    /// <summary>Gets or sets the confirm verifier returned by SETCLIENTID (8 bytes).</summary>
    public byte[] Confirm { get; set; } = new byte[Nfs4.VerifierSize];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetClientIdConfirm;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt64(ClientId);
        writer.WriteOpaqueFixed(Confirm);
    }
}

/// <summary>OPEN: open (and optionally create) a file, establishing open state (CLAIM_NULL only).</summary>
public sealed class Nfs4OpenOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the open-owner sequence value.</summary>
    public uint Seqid { get; set; }

    /// <summary>Gets or sets the requested share access (see <see cref="Nfs4ShareAccess"/>).</summary>
    public uint ShareAccess { get; set; } = Nfs4ShareAccess.Read;

    /// <summary>Gets or sets the requested share deny.</summary>
    public uint ShareDeny { get; set; }

    /// <summary>Gets or sets the owning client identifier.</summary>
    public ulong ClientId { get; set; }

    /// <summary>Gets or sets the opaque open-owner.</summary>
    public byte[] Owner { get; set; } = [];

    /// <summary>Gets or sets whether the file is created if absent.</summary>
    public Nfs4OpenType OpenType { get; set; }

    /// <summary>Gets or sets the create mode (used only when <see cref="OpenType"/> is create).</summary>
    public Nfs4CreateMode CreateMode { get; set; }

    /// <summary>Gets or sets the initial attributes for a create.</summary>
    public Nfs4FAttr CreateAttributes { get; set; }

    /// <summary>Gets or sets the exclusive create verifier (used only for EXCLUSIVE4 creates).</summary>
    public byte[] CreateVerifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Gets or sets the component name to open within the current directory.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets whether this OPEN reclaims previous state during reboot grace.</summary>
    public bool Reclaim { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Open;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32(Seqid);
        writer.WriteUInt32(ShareAccess);
        writer.WriteUInt32(ShareDeny);
        writer.WriteUInt64(ClientId);
        writer.WriteOpaqueVariable(Owner);

        writer.WriteUInt32((uint)OpenType);
        if (OpenType == Nfs4OpenType.Create)
        {
            writer.WriteUInt32((uint)CreateMode);
            if (CreateMode == Nfs4CreateMode.Exclusive)
            {
                writer.WriteOpaqueFixed(CreateVerifier);
            }
            else
            {
                CreateAttributes.WriteTo(ref writer);
            }
        }

        if (Reclaim)
        {
            writer.WriteUInt32(1); // CLAIM_PREVIOUS
            writer.WriteUInt32(0); // OPEN_DELEGATE_NONE
        }
        else
        {
            writer.WriteUInt32(0); // CLAIM_NULL
            writer.WriteString(Name);
        }
    }
}

/// <summary>OPEN_CONFIRM: confirm an open and advance the state sequence.</summary>
public sealed class Nfs4OpenConfirmOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the open state identifier from OPEN.</summary>
    public Nfs4StateId OpenStateId { get; set; }

    /// <summary>Gets or sets the open-owner sequence value.</summary>
    public uint Seqid { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.OpenConfirm;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        OpenStateId.WriteTo(ref writer);
        writer.WriteUInt32(Seqid);
    }
}

/// <summary>OPEN_DOWNGRADE: narrow share access/deny on an open state identifier.</summary>
public sealed class Nfs4OpenDowngradeOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the open state identifier to downgrade.</summary>
    public Nfs4StateId OpenStateId { get; set; }

    /// <summary>Gets or sets the open-owner sequence value.</summary>
    public uint Seqid { get; set; }

    /// <summary>Gets or sets the narrowed share access.</summary>
    public uint ShareAccess { get; set; } = Nfs4ShareAccess.Read;

    /// <summary>Gets or sets the narrowed share deny.</summary>
    public uint ShareDeny { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.OpenDowngrade;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        OpenStateId.WriteTo(ref writer);
        writer.WriteUInt32(Seqid);
        writer.WriteUInt32(ShareAccess);
        writer.WriteUInt32(ShareDeny);
    }
}

/// <summary>CLOSE: release open state for a file.</summary>
public sealed class Nfs4CloseOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the open-owner sequence value.</summary>
    public uint Seqid { get; set; }

    /// <summary>Gets or sets the open state identifier to close.</summary>
    public Nfs4StateId OpenStateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Close;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32(Seqid);
        OpenStateId.WriteTo(ref writer);
    }
}

/// <summary>RENEW: renew a client's lease.</summary>
public sealed class Nfs4RenewOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the client identifier whose lease is renewed.</summary>
    public ulong ClientId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Renew;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteUInt64(ClientId);
}

/// <summary>DELEGRETURN: return a delegation state identifier.</summary>
public sealed class Nfs4DelegReturnOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the delegation state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.DelegReturn;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => StateId.WriteTo(ref writer);
}
