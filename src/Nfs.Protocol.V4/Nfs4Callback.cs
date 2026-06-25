using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>Identifies the NFSv4 callback RPC procedures.</summary>
public enum Nfs4CallbackProcedure
{
    /// <summary>Do nothing (CB_NULL).</summary>
    Null = 0,

    /// <summary>Execute a callback compound (CB_COMPOUND).</summary>
    Compound = 1,
}

/// <summary>Identifies NFSv4 callback operation numbers.</summary>
public enum Nfs4CallbackOp
{
    /// <summary>Recall a delegation (OP_CB_RECALL).</summary>
    Recall = 4,

    /// <summary>Sequence a callback request on a session (OP_CB_SEQUENCE).</summary>
    Sequence = 11,

    /// <summary>Notify a client that a denied lock may now be available (OP_CB_NOTIFY_LOCK).</summary>
    NotifyLock = 13,

    /// <summary>Report completion of an asynchronous copy offload (OP_CB_OFFLOAD).</summary>
    Offload = 15,
}

/// <summary>Callback COMPOUND arguments.</summary>
public sealed class Nfs4CallbackCompoundArgs : IXdrSerializable<Nfs4CallbackCompoundArgs>
{
    /// <summary>Gets or sets the callback tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets or sets the callback minor version.</summary>
    public uint MinorVersion { get; set; }

    /// <summary>Gets or sets the callback identifier from SETCLIENTID.</summary>
    public uint CallbackIdent { get; set; }

    /// <summary>Gets the callback operations.</summary>
    public List<Nfs4CallbackArgOp> Operations { get; } = [];

    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteString(Tag);
        writer.WriteUInt32(MinorVersion);
        writer.WriteUInt32(CallbackIdent);
        writer.WriteUInt32((uint)Operations.Count);
        foreach (Nfs4CallbackArgOp operation in Operations)
        {
            writer.WriteUInt32((uint)operation.Op);
            operation.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    public static Nfs4CallbackCompoundArgs ReadFrom(ref XdrReader reader)
    {
        var args = new Nfs4CallbackCompoundArgs
        {
            Tag = reader.ReadString(1024),
            MinorVersion = reader.ReadUInt32(),
            CallbackIdent = reader.ReadUInt32(),
        };

        uint count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            var op = (Nfs4CallbackOp)reader.ReadUInt32();
            args.Operations.Add(op switch
            {
                Nfs4CallbackOp.Sequence => new Nfs4CallbackSequenceOp
                {
                    SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray(),
                    SequenceId = reader.ReadUInt32(),
                    SlotId = reader.ReadUInt32(),
                    HighestSlotId = reader.ReadUInt32(),
                    CacheThis = reader.ReadBool(),
                },
                Nfs4CallbackOp.Recall => new Nfs4CallbackRecallOp
                {
                    StateId = Nfs4StateId.ReadFrom(ref reader),
                    Truncate = reader.ReadBool(),
                    Handle = Nfs4Handle.ReadFrom(ref reader),
                },
                Nfs4CallbackOp.NotifyLock => new Nfs4CallbackNotifyLockOp
                {
                    Handle = Nfs4Handle.ReadFrom(ref reader),
                    Owner = new Nfs4LockOwner(reader.ReadUInt64(), reader.ReadOpaqueVariable(1024).ToArray()),
                },
                Nfs4CallbackOp.Offload => Nfs4CallbackOffloadOp.ReadFrom(ref reader),
                _ => throw new XdrException($"Unsupported callback operation {(uint)op}."),
            });
        }

        return args;
    }
}

/// <summary>Callback COMPOUND results.</summary>
public sealed class Nfs4CallbackCompoundResult : IXdrSerializable<Nfs4CallbackCompoundResult>
{
    /// <summary>Gets or sets the callback compound status.</summary>
    public Nfs4Status Status { get; set; }

    /// <summary>Gets or sets the echoed callback tag.</summary>
    public string Tag { get; set; } = string.Empty;

    /// <summary>Gets the callback operation results.</summary>
    public List<Nfs4CallbackResOp> Operations { get; } = [];

    /// <summary>Gets the callback operation statuses.</summary>
    public List<Nfs4Status> OperationStatuses { get; } = [];

    /// <inheritdoc/>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        writer.WriteString(Tag);
        if (Operations.Count != 0)
        {
            writer.WriteUInt32((uint)Operations.Count);
            foreach (Nfs4CallbackResOp operation in Operations)
            {
                writer.WriteInt32((int)operation.Op);
                operation.WriteTo(ref writer);
            }

            return;
        }

        writer.WriteUInt32((uint)OperationStatuses.Count);
        foreach (Nfs4Status status in OperationStatuses)
        {
            writer.WriteInt32((int)Nfs4CallbackOp.Recall);
            writer.WriteInt32((int)status);
        }
    }

    /// <inheritdoc/>
    public static Nfs4CallbackCompoundResult ReadFrom(ref XdrReader reader)
    {
        var result = new Nfs4CallbackCompoundResult
        {
            Status = (Nfs4Status)reader.ReadInt32(),
            Tag = reader.ReadString(1024),
        };

        uint count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            var op = (Nfs4CallbackOp)reader.ReadInt32();
            var status = (Nfs4Status)reader.ReadInt32();
            result.OperationStatuses.Add(status);
            result.Operations.Add(op switch
            {
                Nfs4CallbackOp.Sequence => Nfs4CallbackSequenceResult.Read(status, ref reader),
                Nfs4CallbackOp.Recall => new Nfs4CallbackStatusResult(Nfs4CallbackOp.Recall) { Status = status },
                Nfs4CallbackOp.NotifyLock => new Nfs4CallbackStatusResult(Nfs4CallbackOp.NotifyLock) { Status = status },
                Nfs4CallbackOp.Offload => new Nfs4CallbackStatusResult(Nfs4CallbackOp.Offload) { Status = status },
                _ => throw new XdrException($"Unsupported callback result operation {(uint)op}."),
            });
        }

        return result;
    }
}

/// <summary>Base type for callback operation arguments.</summary>
public abstract class Nfs4CallbackArgOp
{
    /// <summary>Gets the callback operation number.</summary>
    public abstract Nfs4CallbackOp Op { get; }

    /// <summary>Encodes the operation arguments.</summary>
    /// <param name="writer">The XDR writer.</param>
    public abstract void WriteTo(ref XdrWriter writer);
}

/// <summary>CB_RECALL: recall a delegation from a client.</summary>
public sealed class Nfs4CallbackRecallOp : Nfs4CallbackArgOp
{
    /// <summary>Gets or sets the delegation state identifier to recall.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <summary>Gets or sets whether the client should truncate modified data.</summary>
    public bool Truncate { get; set; }

    /// <summary>Gets or sets the delegated file handle.</summary>
    public Nfs4Handle Handle { get; set; }

    /// <inheritdoc/>
    public override Nfs4CallbackOp Op => Nfs4CallbackOp.Recall;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteBool(Truncate);
        Handle.WriteTo(ref writer);
    }
}

/// <summary>CB_SEQUENCE: sequence a callback request over an NFSv4.1 session back channel.</summary>
public sealed class Nfs4CallbackSequenceOp : Nfs4CallbackArgOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the per-slot callback sequence value.</summary>
    public uint SequenceId { get; set; }

    /// <summary>Gets or sets the callback slot to use.</summary>
    public uint SlotId { get; set; }

    /// <summary>Gets or sets the highest callback slot the sender will use.</summary>
    public uint HighestSlotId { get; set; }

    /// <summary>Gets or sets whether the client should cache this callback reply.</summary>
    public bool CacheThis { get; set; }

    /// <inheritdoc/>
    public override Nfs4CallbackOp Op => Nfs4CallbackOp.Sequence;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32(SequenceId);
        writer.WriteUInt32(SlotId);
        writer.WriteUInt32(HighestSlotId);
        writer.WriteBool(CacheThis);
    }
}

/// <summary>CB_OFFLOAD: report completion of an asynchronous COPY.</summary>
public sealed class Nfs4CallbackOffloadOp : Nfs4CallbackArgOp
{
    /// <summary>Gets or sets the copy offload state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <summary>Gets or sets the final offload status.</summary>
    public Nfs4Status Status { get; set; }

    /// <summary>Gets or sets the final write response when <see cref="Status"/> is OK.</summary>
    public Nfs4CopyWriteResponse Response { get; set; } = new();

    /// <inheritdoc/>
    public override Nfs4CallbackOp Op => Nfs4CallbackOp.Offload;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteInt32((int)Status);
        if (Status == Nfs4Status.Ok)
        {
            Response.WriteTo(ref writer);
        }
    }

    /// <summary>Decodes CB_OFFLOAD arguments after the operation number.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded operation.</returns>
    public static Nfs4CallbackOffloadOp ReadFrom(ref XdrReader reader)
    {
        var op = new Nfs4CallbackOffloadOp
        {
            StateId = Nfs4StateId.ReadFrom(ref reader),
            Status = (Nfs4Status)reader.ReadInt32(),
        };
        if (op.Status == Nfs4Status.Ok)
        {
            op.Response = Nfs4CopyWriteResponse.ReadFrom(ref reader);
        }

        return op;
    }
}

/// <summary>CB_NOTIFY_LOCK: notify that a previously denied lock may now be acquired.</summary>
public sealed class Nfs4CallbackNotifyLockOp : Nfs4CallbackArgOp
{
    /// <summary>Gets or sets the file handle containing the lock range.</summary>
    public Nfs4Handle Handle { get; set; }

    /// <summary>Gets or sets the lock owner that should retry.</summary>
    public Nfs4LockOwner Owner { get; set; }

    /// <inheritdoc/>
    public override Nfs4CallbackOp Op => Nfs4CallbackOp.NotifyLock;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer)
    {
        Handle.WriteTo(ref writer);
        writer.WriteUInt64(Owner.ClientId);
        writer.WriteOpaqueVariable(Owner.Owner ?? []);
    }
}

/// <summary>Base type for callback operation results.</summary>
public abstract class Nfs4CallbackResOp
{
    /// <summary>Gets the callback operation number.</summary>
    public abstract Nfs4CallbackOp Op { get; }

    /// <summary>Gets or sets the operation status.</summary>
    public Nfs4Status Status { get; set; }

    /// <summary>Encodes the operation result after the operation number.</summary>
    /// <param name="writer">The XDR writer.</param>
    public abstract void WriteTo(ref XdrWriter writer);
}

/// <summary>A callback operation result with only a status body.</summary>
public sealed class Nfs4CallbackStatusResult(Nfs4CallbackOp op) : Nfs4CallbackResOp
{
    /// <inheritdoc/>
    public override Nfs4CallbackOp Op { get; } = op;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer) => writer.WriteInt32((int)Status);
}

/// <summary>CB_SEQUENCE result.</summary>
public sealed class Nfs4CallbackSequenceResult : Nfs4CallbackResOp
{
    /// <summary>Gets or sets the session identifier (16 bytes).</summary>
    public byte[] SessionId { get; set; } = new byte[Nfs4.SessionIdSize];

    /// <summary>Gets or sets the per-slot callback sequence value.</summary>
    public uint SequenceId { get; set; }

    /// <summary>Gets or sets the callback slot used.</summary>
    public uint SlotId { get; set; }

    /// <summary>Gets or sets the highest slot in use.</summary>
    public uint HighestSlotId { get; set; }

    /// <summary>Gets or sets the highest slot the client wants the server to use.</summary>
    public uint TargetHighestSlotId { get; set; }

    /// <inheritdoc/>
    public override Nfs4CallbackOp Op => Nfs4CallbackOp.Sequence;

    /// <inheritdoc/>
    public override void WriteTo(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (Status != Nfs4Status.Ok)
        {
            return;
        }

        writer.WriteOpaqueFixed(SessionId);
        writer.WriteUInt32(SequenceId);
        writer.WriteUInt32(SlotId);
        writer.WriteUInt32(HighestSlotId);
        writer.WriteUInt32(TargetHighestSlotId);
    }

    /// <summary>Decodes a CB_SEQUENCE result after its status.</summary>
    /// <param name="status">The already-decoded operation status.</param>
    /// <param name="reader">The XDR reader.</param>
    /// <returns>The decoded result.</returns>
    public static Nfs4CallbackSequenceResult Read(Nfs4Status status, ref XdrReader reader)
    {
        var result = new Nfs4CallbackSequenceResult { Status = status };
        if (status != Nfs4Status.Ok)
        {
            return result;
        }

        result.SessionId = reader.ReadOpaqueFixed(Nfs4.SessionIdSize).ToArray();
        result.SequenceId = reader.ReadUInt32();
        result.SlotId = reader.ReadUInt32();
        result.HighestSlotId = reader.ReadUInt32();
        result.TargetHighestSlotId = reader.ReadUInt32();
        return result;
    }
}
