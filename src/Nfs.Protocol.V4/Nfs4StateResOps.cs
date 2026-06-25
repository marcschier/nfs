using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>The result of SETCLIENTID (the client identifier and confirm verifier on success).</summary>
public sealed class Nfs4SetClientIdResult : Nfs4ResOp
{
    /// <summary>Gets or sets the server-assigned client identifier.</summary>
    public ulong ClientId { get; set; }

    /// <summary>Gets or sets the confirm verifier to echo in SETCLIENTID_CONFIRM (8 bytes).</summary>
    public byte[] ConfirmVerifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetClientId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt64(ClientId);
            writer.WriteOpaqueFixed(ConfirmVerifier);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            ClientId = reader.ReadUInt64();
            ConfirmVerifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray();
        }
    }
}

/// <summary>The result of OPEN (the open state identifier and related data on success).</summary>
public sealed class Nfs4OpenResult : Nfs4ResOp
{
    /// <summary>No delegation was granted (OPEN_DELEGATE_NONE).</summary>
    public const uint DelegationNone = 0;

    /// <summary>A read delegation was granted (OPEN_DELEGATE_READ).</summary>
    public const uint DelegationRead = 1;

    /// <summary>A write delegation was granted (OPEN_DELEGATE_WRITE).</summary>
    public const uint DelegationWrite = 2;

    /// <summary>The OPEN result flag indicating the open must be confirmed (OPEN4_RESULT_CONFIRM).</summary>
    public const uint ResultConfirm = 0x00000002;

    /// <summary>The OPEN result flag indicating no OPEN_CONFIRM is required (OPEN4_RESULT_LOCKTYPE_POSIX).</summary>
    public const uint ResultLockTypePosix = 0x00000004;

    /// <summary>Gets or sets the open state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <summary>Gets or sets the directory change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <summary>Gets or sets the result flags (e.g. whether OPEN_CONFIRM is required).</summary>
    public uint ResultFlags { get; set; }

    /// <summary>Gets or sets the bitmap of attributes that were set.</summary>
    public Nfs4Bitmap AttributesSet { get; set; }

    /// <summary>Gets or sets the granted delegation type.</summary>
    public uint DelegationType { get; set; }

    /// <summary>Gets or sets the read delegation state identifier.</summary>
    public Nfs4StateId DelegationStateId { get; set; }

    /// <summary>Gets or sets whether the delegation is being recalled immediately.</summary>
    public bool DelegationRecall { get; set; }

    /// <summary>Gets or sets the write delegation space limit in bytes.</summary>
    public ulong DelegationSpaceLimit { get; set; } = ulong.MaxValue;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Open;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        StateId.WriteTo(ref writer);
        ChangeInfo.WriteTo(ref writer);
        writer.WriteUInt32(ResultFlags);
        AttributesSet.WriteTo(ref writer);
        writer.WriteUInt32(DelegationType);
        if (DelegationType is DelegationRead or DelegationWrite)
        {
            DelegationStateId.WriteTo(ref writer);
            writer.WriteBool(DelegationRecall);
            if (DelegationType == DelegationWrite)
            {
                writer.WriteUInt32(1); // NFS_LIMIT_SIZE
                writer.WriteUInt64(DelegationSpaceLimit);
            }

            writer.WriteUInt32(0); // nfsace4 type
            writer.WriteUInt32(0); // nfsace4 flag
            writer.WriteUInt32(uint.MaxValue); // nfsace4 access mask
            writer.WriteString("OWNER@");
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        StateId = Nfs4StateId.ReadFrom(ref reader);
        ChangeInfo = Nfs4ChangeInfo.ReadFrom(ref reader);
        ResultFlags = reader.ReadUInt32();
        AttributesSet = Nfs4Bitmap.ReadFrom(ref reader);
        DelegationType = reader.ReadUInt32();
        if (DelegationType is DelegationRead or DelegationWrite)
        {
            DelegationStateId = Nfs4StateId.ReadFrom(ref reader);
            DelegationRecall = reader.ReadBool();
            if (DelegationType == DelegationWrite)
            {
                uint limitBy = reader.ReadUInt32();
                if (limitBy == 1)
                {
                    DelegationSpaceLimit = reader.ReadUInt64();
                }
                else if (limitBy == 2)
                {
                    _ = reader.ReadUInt32();
                    _ = reader.ReadUInt32();
                    DelegationSpaceLimit = ulong.MaxValue;
                }
                else
                {
                    throw new XdrException($"Unsupported OPEN write delegation limit {limitBy}.");
                }
            }

            _ = reader.ReadUInt32(); // nfsace4 type
            _ = reader.ReadUInt32(); // nfsace4 flag
            _ = reader.ReadUInt32(); // nfsace4 access mask
            _ = reader.ReadString(1024);
        }
        else if (DelegationType != DelegationNone)
        {
            throw new XdrException($"Unsupported OPEN delegation type {DelegationType}.");
        }
    }
}

/// <summary>The result of OPEN_CONFIRM or CLOSE (an updated state identifier on success).</summary>
public sealed class Nfs4StateIdResult : Nfs4ResOp
{
    /// <summary>Creates a state-identifier result for <paramref name="op"/>.</summary>
    /// <param name="op">The operation code (OPEN_CONFIRM or CLOSE).</param>
    public Nfs4StateIdResult(Nfs4Op op) => Op = op;

    /// <summary>Gets or sets the updated state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op { get; }

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
