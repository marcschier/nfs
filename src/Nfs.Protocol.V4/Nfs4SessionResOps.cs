using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>The result of EXCHANGE_ID (the client identifier and sequence on success).</summary>
public sealed class Nfs4ExchangeIdResult : Nfs4ResOp
{
    /// <summary>Gets or sets the server-assigned client identifier.</summary>
    public ulong ClientId { get; set; }

    /// <summary>Gets or sets the sequence value to use for CREATE_SESSION.</summary>
    public uint SequenceId { get; set; }

    /// <summary>Gets or sets the exchange flags returned by the server.</summary>
    public uint Flags { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ExchangeId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        writer.WriteUInt64(ClientId);
        writer.WriteUInt32(SequenceId);
        writer.WriteUInt32(Flags);
        writer.WriteUInt32(0); // state_protect4_r: SP4_NONE
        writer.WriteUInt64(0); // server_owner4.so_minor_id
        writer.WriteOpaqueVariable("nfs-dotnet"u8); // server_owner4.so_major_id
        writer.WriteOpaqueVariable("nfs-dotnet"u8); // eir_server_scope
        writer.WriteUInt32(0); // eir_server_impl_id<1>: empty
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        ClientId = reader.ReadUInt64();
        SequenceId = reader.ReadUInt32();
        Flags = reader.ReadUInt32();
        _ = reader.ReadUInt32();                 // state_protect4_r: SP4_NONE
        _ = reader.ReadUInt64();                 // server_owner4.so_minor_id
        _ = reader.ReadOpaqueVariable(1024);     // server_owner4.so_major_id
        _ = reader.ReadOpaqueVariable(1024);     // eir_server_scope
        uint implCount = reader.ReadUInt32();    // eir_server_impl_id<1>
        for (uint i = 0; i < implCount; i++)
        {
            _ = reader.ReadOpaqueVariable(1024); // nii_domain
            _ = reader.ReadOpaqueVariable(1024); // nii_name
            _ = reader.ReadInt64();              // nii_date.seconds
            _ = reader.ReadUInt32();             // nii_date.nseconds
        }
    }
}

/// <summary>The result of CREATE_SESSION (the session identifier and channel attributes on success).</summary>
public sealed class Nfs4CreateSessionResult : Nfs4ResOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the sequence value echoed back.</summary>
    public uint Sequence { get; set; }

    /// <summary>Gets or sets the session flags.</summary>
    public uint Flags { get; set; }

    /// <summary>Gets or sets the fore-channel attributes the server accepted.</summary>
    public Nfs4ChannelAttributes ForeChannel { get; set; }

    /// <summary>Gets or sets the back-channel attributes the server accepted.</summary>
    public Nfs4ChannelAttributes BackChannel { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.CreateSession;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(Flags);
        ForeChannel.WriteTo(ref writer);
        BackChannel.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray();
        Sequence = reader.ReadUInt32();
        Flags = reader.ReadUInt32();
        ForeChannel = Nfs4ChannelAttributes.ReadFrom(ref reader);
        BackChannel = Nfs4ChannelAttributes.ReadFrom(ref reader);
    }
}

/// <summary>The result of SEQUENCE (slot and session bookkeeping on success).</summary>
public sealed class Nfs4SequenceResult : Nfs4ResOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the per-slot sequence value.</summary>
    public uint SequenceId { get; set; }

    /// <summary>Gets or sets the slot used.</summary>
    public uint SlotId { get; set; }

    /// <summary>Gets or sets the highest slot in use.</summary>
    public uint HighestSlotId { get; set; }

    /// <summary>Gets or sets the highest slot the server would like the client to use.</summary>
    public uint TargetHighestSlotId { get; set; }

    /// <summary>Gets or sets the session status flags.</summary>
    public uint StatusFlags { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Sequence;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32(SequenceId);
        writer.WriteUInt32(SlotId);
        writer.WriteUInt32(HighestSlotId);
        writer.WriteUInt32(TargetHighestSlotId);
        writer.WriteUInt32(StatusFlags);
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray();
        SequenceId = reader.ReadUInt32();
        SlotId = reader.ReadUInt32();
        HighestSlotId = reader.ReadUInt32();
        TargetHighestSlotId = reader.ReadUInt32();
        StatusFlags = reader.ReadUInt32();
    }
}
