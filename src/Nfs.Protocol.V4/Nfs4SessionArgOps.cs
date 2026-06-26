using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>Channel attributes negotiated for a session (<c>channel_attrs4</c>, RFC 8881).</summary>
public record struct Nfs4ChannelAttributes : IXdrSerializable<Nfs4ChannelAttributes>
{
    /// <summary>The header padding size.</summary>
    public uint HeaderPadSize { get; set; }

    /// <summary>The maximum request size, in bytes.</summary>
    public uint MaxRequestSize { get; set; }

    /// <summary>The maximum response size, in bytes.</summary>
    public uint MaxResponseSize { get; set; }

    /// <summary>The maximum cached response size, in bytes.</summary>
    public uint MaxResponseSizeCached { get; set; }

    /// <summary>The maximum number of operations per request.</summary>
    public uint MaxOperations { get; set; }

    /// <summary>The maximum number of outstanding requests (the slot count).</summary>
    public uint MaxRequests { get; set; }

    /// <summary>Gets a set of reasonable default attributes with <paramref name="slots"/> slots.</summary>
    /// <param name="slots">The number of slots.</param>
    /// <returns>The attributes.</returns>
    public static Nfs4ChannelAttributes Default(uint slots) => new()
    {
        HeaderPadSize = 0,
        MaxRequestSize = 1 << 20,
        MaxResponseSize = 1 << 20,
        MaxResponseSizeCached = 8192,
        MaxOperations = 64,
        MaxRequests = slots,
    };

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32(HeaderPadSize);
        writer.WriteUInt32(MaxRequestSize);
        writer.WriteUInt32(MaxResponseSize);
        writer.WriteUInt32(MaxResponseSizeCached);
        writer.WriteUInt32(MaxOperations);
        writer.WriteUInt32(MaxRequests);
        writer.WriteUInt32(0); // ca_rdma_ird<1>: empty array
    }

    /// <inheritdoc/>
    public static Nfs4ChannelAttributes ReadFrom(ref XdrReader reader)
    {
        var attributes = new Nfs4ChannelAttributes
        {
            HeaderPadSize = reader.ReadUInt32(),
            MaxRequestSize = reader.ReadUInt32(),
            MaxResponseSize = reader.ReadUInt32(),
            MaxResponseSizeCached = reader.ReadUInt32(),
            MaxOperations = reader.ReadUInt32(),
            MaxRequests = reader.ReadUInt32(),
        };

        uint rdmaCount = reader.ReadUInt32();
        for (uint i = 0; i < rdmaCount; i++)
        {
            _ = reader.ReadUInt32();
        }

        return attributes;
    }
}

/// <summary>Channel directions requested by BIND_CONN_TO_SESSION (<c>channel_dir_from_client4</c>).</summary>
public enum Nfs4ChannelDirectionFromClient
{
    /// <summary>Bind the connection for fore-channel traffic.</summary>
    Fore = 1,

    /// <summary>Bind the connection for back-channel traffic.</summary>
    Back = 2,

    /// <summary>Bind for fore-channel traffic, or both if the server chooses.</summary>
    ForeOrBoth = 3,

    /// <summary>Bind for back-channel traffic, or both if the server chooses.</summary>
    BackOrBoth = 4,
}

/// <summary>BACKCHANNEL_CTL: update callback program and security parameters for the current session.</summary>
public sealed class Nfs4BackchannelCtlOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the callback RPC program number.</summary>
    public uint CallbackProgram { get; set; }

    /// <summary>Gets the callback security flavors.</summary>
    public List<int> CallbackSecurityFlavors { get; } = [0];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.BackchannelCtl;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32(CallbackProgram);
        writer.WriteUInt32((uint)CallbackSecurityFlavors.Count);
        foreach (int flavor in CallbackSecurityFlavors)
        {
            writer.WriteInt32(flavor);
        }
    }
}

/// <summary>BIND_CONN_TO_SESSION: bind this transport connection to a session.</summary>
public sealed class Nfs4BindConnToSessionOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the channel direction requested by the client.</summary>
    public Nfs4ChannelDirectionFromClient Direction { get; set; } = Nfs4ChannelDirectionFromClient.Fore;

    /// <summary>Gets or sets whether this connection should be used in RDMA mode.</summary>
    public bool UseConnectionInRdmaMode { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.BindConnToSession;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32((uint)Direction);
        writer.WriteBool(UseConnectionInRdmaMode);
    }
}

/// <summary>EXCHANGE_ID: establish or update a client identifier (version 4.1).</summary>
public sealed class Nfs4ExchangeIdOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the client's boot verifier (8 bytes).</summary>
    public byte[] Verifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Gets or sets the client's opaque owner identifier.</summary>
    public byte[] OwnerId { get; set; } = [];

    /// <summary>Gets or sets the exchange flags.</summary>
    public uint Flags { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ExchangeId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(Verifier);
        writer.WriteOpaqueVariable(OwnerId);
        writer.WriteUInt32(Flags);
        writer.WriteUInt32(0); // state_protect4_a: SP4_NONE
        writer.WriteUInt32(0); // nfs_impl_id4<1>: empty array
    }
}

/// <summary>CREATE_SESSION: create a session for a client (version 4.1).</summary>
public sealed class Nfs4CreateSessionOp : Nfs4ArgOp
{
    /// <summary>CREATE_SESSION4_FLAG_CONN_BACK_CHAN.</summary>
    public const uint FlagConnectionBackChannel = 0x00000002;

    /// <summary>Gets or sets the client identifier from EXCHANGE_ID.</summary>
    public ulong ClientId { get; set; }

    /// <summary>Gets or sets the EXCHANGE_ID sequence value.</summary>
    public uint Sequence { get; set; }

    /// <summary>Gets or sets the session flags.</summary>
    public uint Flags { get; set; }

    /// <summary>Gets or sets the fore-channel attributes.</summary>
    public Nfs4ChannelAttributes ForeChannel { get; set; } = Nfs4ChannelAttributes.Default(16);

    /// <summary>Gets or sets the back-channel attributes.</summary>
    public Nfs4ChannelAttributes BackChannel { get; set; } = Nfs4ChannelAttributes.Default(1);

    /// <summary>Gets or sets the callback program number.</summary>
    public uint CallbackProgram { get; set; }

    /// <summary>Gets the callback security flavors from csa_sec_parms.</summary>
    public List<int> CallbackSecurityFlavors { get; } = [0];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.CreateSession;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt64(ClientId);
        writer.WriteUInt32(Sequence);
        writer.WriteUInt32(Flags);
        ForeChannel.WriteTo(ref writer);
        BackChannel.WriteTo(ref writer);
        writer.WriteUInt32(CallbackProgram);
        writer.WriteUInt32((uint)CallbackSecurityFlavors.Count);
        foreach (int flavor in CallbackSecurityFlavors)
        {
            writer.WriteInt32(flavor);
        }
    }
}

/// <summary>SEQUENCE: lead a version 4.1 COMPOUND with per-slot sequencing.</summary>
public sealed class Nfs4SequenceOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the per-slot sequence value.</summary>
    public uint SequenceId { get; set; }

    /// <summary>Gets or sets the slot to use.</summary>
    public uint SlotId { get; set; }

    /// <summary>Gets or sets the highest slot the client will use.</summary>
    public uint HighestSlotId { get; set; }

    /// <summary>Gets or sets whether the server should cache this request's reply.</summary>
    public bool CacheThis { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Sequence;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32(SequenceId);
        writer.WriteUInt32(SlotId);
        writer.WriteUInt32(HighestSlotId);
        writer.WriteBool(CacheThis);
    }
}

/// <summary>DESTROY_SESSION: destroy a session (version 4.1).</summary>
public sealed class Nfs4DestroySessionOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the session identifier to destroy (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.DestroySession;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteOpaqueFixed(SessionId);
}

/// <summary>FREE_STATEID: release a lock state identifier with no active locks.</summary>
public sealed class Nfs4FreeStateIdOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the state identifier to release.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.FreeStateId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => StateId.WriteTo(ref writer);
}

/// <summary>TEST_STATEID: check one or more state identifiers.</summary>
public sealed class Nfs4TestStateIdOp : Nfs4ArgOp
{
    /// <summary>Gets the state identifiers to test.</summary>
    public List<Nfs4StateId> StateIds { get; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.TestStateId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)StateIds.Count);
        foreach (Nfs4StateId stateId in StateIds)
        {
            stateId.WriteTo(ref writer);
        }
    }
}

/// <summary>DESTROY_CLIENTID: destroy a client identifier (version 4.1).</summary>
public sealed class Nfs4DestroyClientIdOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the client identifier to destroy.</summary>
    public ulong ClientId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.DestroyClientId;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteUInt64(ClientId);
}

/// <summary>RECLAIM_COMPLETE: signal that reclaim is finished (version 4.1).</summary>
public sealed class Nfs4ReclaimCompleteOp : Nfs4ArgOp
{
    /// <summary>Gets or sets whether the reclaim is for one file system only.</summary>
    public bool OneFileSystem { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReclaimComplete;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteBool(OneFileSystem);
}
