using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>What an OP_SEEK searches for (<c>data_content4</c>, RFC 7862).</summary>
public enum Nfs4ContentType
{
    /// <summary>The next region containing data (NFS4_CONTENT_DATA).</summary>
    Data = 0,

    /// <summary>The next hole (unwritten region) (NFS4_CONTENT_HOLE).</summary>
    Hole = 1,
}

/// <summary>The discriminator for an NFSv4.2 server network location.</summary>
public enum Nfs4NetLocationType
{
    /// <summary>A UTF-8 host name.</summary>
    Name = 1,

    /// <summary>A URL string.</summary>
    Url = 2,

    /// <summary>An RPC netid plus universal address.</summary>
    NetAddress = 3,
}

/// <summary>An NFSv4.2 server network location (<c>netloc4</c>).</summary>
public sealed class Nfs4NetLocation
{
    /// <summary>Gets or sets the location discriminator.</summary>
    public Nfs4NetLocationType Type { get; set; } = Nfs4NetLocationType.NetAddress;

    /// <summary>Gets or sets a host name or URL for <see cref="Type"/> values Name or Url.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Gets or sets the RPC netid for <see cref="Nfs4NetLocationType.NetAddress"/>.</summary>
    public string NetId { get; set; } = "tcp";

    /// <summary>Gets or sets the universal address for <see cref="Nfs4NetLocationType.NetAddress"/>.</summary>
    public string Address { get; set; } = "127.0.0.1.0.0";

    /// <summary>Writes the location.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)Type);
        if (Type == Nfs4NetLocationType.NetAddress)
        {
            writer.WriteString(NetId);
            writer.WriteString(Address);
            return;
        }

        writer.WriteString(Value);
    }

    /// <summary>Reads a location.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded location.</returns>
    public static Nfs4NetLocation ReadFrom(ref XdrReader reader)
    {
        var type = (Nfs4NetLocationType)reader.ReadUInt32();
        if (type == Nfs4NetLocationType.NetAddress)
        {
            return new Nfs4NetLocation
            {
                Type = type,
                NetId = reader.ReadString(1024),
                Address = reader.ReadString(1024),
            };
        }

        return new Nfs4NetLocation
        {
            Type = type,
            Value = reader.ReadString(1024),
        };
    }
}

/// <summary>COPY: copy data from a saved source file to the current destination file (version 4.2).</summary>
public sealed class Nfs4CopyOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the source state identifier (anonymous is accepted).</summary>
    public Nfs4StateId SourceStateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the destination state identifier (anonymous is accepted).</summary>
    public Nfs4StateId DestinationStateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the source byte offset.</summary>
    public ulong SourceOffset { get; set; }

    /// <summary>Gets or sets the destination byte offset.</summary>
    public ulong DestinationOffset { get; set; }

    /// <summary>Gets or sets the number of bytes to copy.</summary>
    public ulong Count { get; set; }

    /// <summary>Gets or sets whether consecutive byte copying is requested.</summary>
    public bool Consecutive { get; set; }

    /// <summary>Gets or sets whether synchronous copying is requested.</summary>
    public bool Synchronous { get; set; } = true;

    /// <summary>Gets the source server list for inter-server COPY requests.</summary>
    public List<Nfs4NetLocation> SourceServers { get; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Copy;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        SourceStateId.WriteTo(ref writer);
        DestinationStateId.WriteTo(ref writer);
        writer.WriteUInt64(SourceOffset);
        writer.WriteUInt64(DestinationOffset);
        writer.WriteUInt64(Count);
        writer.WriteBool(Consecutive);
        writer.WriteBool(Synchronous);
        writer.WriteUInt32((uint)SourceServers.Count);
        foreach (Nfs4NetLocation sourceServer in SourceServers)
        {
            sourceServer.WriteTo(ref writer);
        }
    }
}

/// <summary>COPY_NOTIFY: authorize a destination server to copy from the current source file.</summary>
public sealed class Nfs4CopyNotifyOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the source state identifier.</summary>
    public Nfs4StateId SourceStateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the destination server identity.</summary>
    public Nfs4NetLocation Destination { get; set; } = new();

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.CopyNotify;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        SourceStateId.WriteTo(ref writer);
        Destination.WriteTo(ref writer);
    }
}

/// <summary>OFFLOAD_CANCEL: cancel an asynchronous copy offload.</summary>
public sealed class Nfs4OffloadCancelOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the offload state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.OffloadCancel;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => StateId.WriteTo(ref writer);
}

/// <summary>OFFLOAD_STATUS: query an asynchronous copy offload.</summary>
public sealed class Nfs4OffloadStatusOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the offload state identifier.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.OffloadStatus;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => StateId.WriteTo(ref writer);
}

/// <summary>ALLOCATE: reserve (and zero-fill) space in the current file (version 4.2).</summary>
public sealed class Nfs4AllocateOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the state identifier (anonymous is accepted).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the length to allocate.</summary>
    public ulong Length { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Allocate;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
    }
}

/// <summary>DEALLOCATE: free (punch a hole in) space in the current file (version 4.2).</summary>
public sealed class Nfs4DeallocateOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the state identifier (anonymous is accepted).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the length to free.</summary>
    public ulong Length { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Deallocate;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
    }
}

/// <summary>READ_PLUS: read file data and sparse-file metadata (version 4.2).</summary>
public sealed class Nfs4ReadPlusOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the read state identifier (anonymous is accepted).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the byte count.</summary>
    public uint Count { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadPlus;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt32(Count);
    }
}

/// <summary>SEEK: find the next data region or hole in the current file (version 4.2).</summary>
public sealed class Nfs4SeekOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the state identifier (anonymous is accepted).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset to search from.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets what to search for.</summary>
    public Nfs4ContentType What { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Seek;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt32((uint)What);
    }
}

/// <summary>CLONE: clone data from a saved source file to the current destination file (version 4.2).</summary>
public sealed class Nfs4CloneOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the source state identifier (anonymous is accepted).</summary>
    public Nfs4StateId SourceStateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the destination state identifier (anonymous is accepted).</summary>
    public Nfs4StateId DestinationStateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the source byte offset.</summary>
    public ulong SourceOffset { get; set; }

    /// <summary>Gets or sets the destination byte offset.</summary>
    public ulong DestinationOffset { get; set; }

    /// <summary>Gets or sets the number of bytes to clone.</summary>
    public ulong Count { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Clone;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        SourceStateId.WriteTo(ref writer);
        DestinationStateId.WriteTo(ref writer);
        writer.WriteUInt64(SourceOffset);
        writer.WriteUInt64(DestinationOffset);
        writer.WriteUInt64(Count);
    }
}

/// <summary>The successful COPY write response (<c>write_response4</c>, RFC 7862).</summary>
public sealed class Nfs4CopyWriteResponse
{
    /// <summary>Gets or sets the optional callback id for asynchronous copy completion.</summary>
    public Nfs4StateId? CallbackId { get; set; }

    /// <summary>Gets or sets the number of bytes written.</summary>
    public ulong Count { get; set; }

    /// <summary>Gets or sets how durably the data was committed.</summary>
    public uint Committed { get; set; }

    /// <summary>Gets or sets the write verifier (8 bytes).</summary>
    public byte[] Verifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Writes the response.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        if (CallbackId is { } callbackId)
        {
            writer.WriteUInt32(1);
            callbackId.WriteTo(ref writer);
        }
        else
        {
            writer.WriteUInt32(0);
        }

        writer.WriteUInt64(Count);
        writer.WriteUInt32(Committed);
        writer.WriteOpaqueFixed(Verifier);
    }

    /// <summary>Reads the response.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded response.</returns>
    public static Nfs4CopyWriteResponse ReadFrom(ref XdrReader reader)
    {
        uint callbackCount = reader.ReadUInt32();
        if (callbackCount > 1)
        {
            throw new XdrException("COPY write callback id count is implausibly large.");
        }

        Nfs4StateId? callbackId = null;
        for (uint i = 0; i < callbackCount; i++)
        {
            callbackId = Nfs4StateId.ReadFrom(ref reader);
        }

        return new Nfs4CopyWriteResponse
        {
            CallbackId = callbackId,
            Count = reader.ReadUInt64(),
            Committed = reader.ReadUInt32(),
            Verifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray(),
        };
    }
}

/// <summary>The result of COPY_NOTIFY.</summary>
public sealed class Nfs4CopyNotifyResult : Nfs4ResOp
{
    /// <summary>Gets or sets the copy authorization lease time.</summary>
    public Nfs4Time LeaseTime { get; set; }

    /// <summary>Gets or sets the copy state identifier to use as the COPY source stateid.</summary>
    public Nfs4StateId StateId { get; set; }

    /// <summary>Gets the returned source network locations.</summary>
    public List<Nfs4NetLocation> SourceLocations { get; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.CopyNotify;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            LeaseTime.WriteTo(ref writer);
            StateId.WriteTo(ref writer);
            writer.WriteUInt32((uint)SourceLocations.Count);
            foreach (Nfs4NetLocation source in SourceLocations)
            {
                source.WriteTo(ref writer);
            }
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        LeaseTime = Nfs4Time.ReadFrom(ref reader);
        StateId = Nfs4StateId.ReadFrom(ref reader);
        uint count = reader.ReadUInt32();
        if (count > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("COPY_NOTIFY source location count is implausibly large.");
        }

        for (uint i = 0; i < count; i++)
        {
            SourceLocations.Add(Nfs4NetLocation.ReadFrom(ref reader));
        }
    }
}

/// <summary>The result of OFFLOAD_STATUS.</summary>
public sealed class Nfs4OffloadStatusResult : Nfs4ResOp
{
    /// <summary>Gets or sets the number of bytes copied so far.</summary>
    public ulong Count { get; set; }

    /// <summary>Gets or sets whether the offload has completed.</summary>
    public bool Complete { get; set; }

    /// <summary>Gets or sets the final status when <see cref="Complete"/> is true.</summary>
    public Nfs4Status CompleteStatus { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.OffloadStatus;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt64(Count);
            writer.WriteUInt32(Complete ? 1u : 0u);
            if (Complete)
            {
                writer.WriteInt32((int)CompleteStatus);
            }
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        Count = reader.ReadUInt64();
        Complete = reader.ReadUInt32() != 0;
        if (Complete)
        {
            CompleteStatus = (Nfs4Status)reader.ReadInt32();
        }
    }
}

/// <summary>The result of COPY.</summary>
public sealed class Nfs4CopyResult : Nfs4ResOp
{
    /// <summary>Gets or sets the write response returned by the server.</summary>
    public Nfs4CopyWriteResponse Response { get; set; } = new();

    /// <summary>Gets or sets whether the copy was consecutive.</summary>
    public bool Consecutive { get; set; }

    /// <summary>Gets or sets whether the copy completed synchronously.</summary>
    public bool Synchronous { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Copy;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt32(0); // cr_callback_id<1>: no callback.
            Response.WriteTo(ref writer);
            writer.WriteBool(Consecutive);
            writer.WriteBool(Synchronous);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            uint callbackCount = reader.ReadUInt32();
            if (callbackCount > 1)
            {
                throw new XdrException("COPY callback id count is implausibly large.");
            }

            for (uint i = 0; i < callbackCount; i++)
            {
                _ = reader.ReadUInt32();
            }

            Response = Nfs4CopyWriteResponse.ReadFrom(ref reader);
            Consecutive = reader.ReadBool();
            Synchronous = reader.ReadBool();
        }
    }
}

/// <summary>A single READ_PLUS content segment.</summary>
public abstract class Nfs4ReadPlusContent
{
    /// <summary>Gets the content type discriminator.</summary>
    public abstract Nfs4ContentType Type { get; }

    /// <summary>Writes the content body after the discriminator.</summary>
    /// <param name="writer">The writer.</param>
    public abstract void WriteBody(ref XdrWriter writer);
}

/// <summary>A READ_PLUS data segment.</summary>
public sealed class Nfs4ReadPlusData : Nfs4ReadPlusContent
{
    /// <summary>Gets or sets the segment byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the data bytes.</summary>
    public byte[] Data { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4ContentType Type => Nfs4ContentType.Data;

    /// <inheritdoc/>
    public override void WriteBody(ref XdrWriter writer)
    {
        writer.WriteUInt64(Offset);
        writer.WriteOpaqueVariable(Data);
    }
}

/// <summary>A READ_PLUS hole segment.</summary>
public sealed class Nfs4ReadPlusHole : Nfs4ReadPlusContent
{
    /// <summary>Gets or sets the hole byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the hole length.</summary>
    public ulong Length { get; set; }

    /// <inheritdoc/>
    public override Nfs4ContentType Type => Nfs4ContentType.Hole;

    /// <inheritdoc/>
    public override void WriteBody(ref XdrWriter writer)
    {
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
    }
}

/// <summary>The result of READ_PLUS.</summary>
public sealed class Nfs4ReadPlusResult : Nfs4ResOp
{
    /// <summary>Gets or sets whether the read reached end of file.</summary>
    public bool Eof { get; set; }

    /// <summary>Gets or sets the returned content segments.</summary>
    public Nfs4ReadPlusContent[] Contents { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadPlus;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(Eof);
            writer.WriteUInt32((uint)Contents.Length);
            foreach (Nfs4ReadPlusContent content in Contents)
            {
                writer.WriteUInt32((uint)content.Type);
                content.WriteBody(ref writer);
            }
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        Eof = reader.ReadBool();
        uint count = reader.ReadUInt32();
        if (count > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("READ_PLUS content count is implausibly large.");
        }

        Contents = new Nfs4ReadPlusContent[count];
        for (uint i = 0; i < count; i++)
        {
            Contents[i] = (Nfs4ContentType)reader.ReadUInt32() switch
            {
                Nfs4ContentType.Data => new Nfs4ReadPlusData
                {
                    Offset = reader.ReadUInt64(),
                    Data = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray(),
                },
                Nfs4ContentType.Hole => new Nfs4ReadPlusHole
                {
                    Offset = reader.ReadUInt64(),
                    Length = reader.ReadUInt64(),
                },
                _ => throw new XdrException("Unsupported READ_PLUS content type."),
            };
        }
    }
}

/// <summary>The result of SEEK (the found offset and end-of-file flag on success).</summary>
public sealed class Nfs4SeekResult : Nfs4ResOp
{
    /// <summary>Gets or sets whether the search reached the end of the file.</summary>
    public bool Eof { get; set; }

    /// <summary>Gets or sets the offset of the next data region or hole.</summary>
    public ulong Offset { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Seek;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(Eof);
            writer.WriteUInt64(Offset);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Eof = reader.ReadBool();
            Offset = reader.ReadUInt64();
        }
    }
}


/// <summary>SETXATTR creation/replacement mode.</summary>
public enum Nfs4SetXattrOption
{
    /// <summary>Create or replace an xattr.</summary>
    Either = 0,

    /// <summary>Create an xattr and fail if it already exists.</summary>
    Create = 1,

    /// <summary>Replace an xattr and fail if it does not exist.</summary>
    Replace = 2,
}

/// <summary>GETXATTR: read an extended attribute value.</summary>
public sealed class Nfs4GetXattrOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the xattr name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteString(Name);
}

/// <summary>SETXATTR: create or replace an extended attribute value.</summary>
public sealed class Nfs4SetXattrOp : Nfs4ArgOp
{
    /// <summary>Gets or sets how existing or missing xattrs are handled.</summary>
    public Nfs4SetXattrOption Option { get; set; }

    /// <summary>Gets or sets the xattr name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the xattr value.</summary>
    public byte[] Value { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)Option);
        writer.WriteString(Name);
        writer.WriteOpaqueVariable(Value);
    }
}

/// <summary>LISTXATTRS: enumerate extended attribute names.</summary>
public sealed class Nfs4ListXattrsOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the listing cookie.</summary>
    public ulong Cookie { get; set; }

    /// <summary>Gets or sets the maximum encoded result byte count.</summary>
    public uint MaxCount { get; set; } = Nfs4.MaxIoSize;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ListXattrs;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt64(Cookie);
        writer.WriteUInt32(MaxCount);
    }
}

/// <summary>REMOVEXATTR: remove an extended attribute value.</summary>
public sealed class Nfs4RemoveXattrOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the xattr name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.RemoveXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteString(Name);
}

/// <summary>The result of GETXATTR.</summary>
public sealed class Nfs4GetXattrResult : Nfs4ResOp
{
    /// <summary>Gets or sets the xattr value.</summary>
    public byte[] Value { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteOpaqueVariable(Value);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Value = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray();
        }
    }
}

/// <summary>The result of SETXATTR.</summary>
public sealed class Nfs4SetXattrResult : Nfs4ResOp
{
    /// <summary>Gets or sets the object change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            ChangeInfo.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            ChangeInfo = Nfs4ChangeInfo.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of LISTXATTRS.</summary>
public sealed class Nfs4ListXattrsResult : Nfs4ResOp
{
    /// <summary>Gets or sets the resume cookie.</summary>
    public ulong Cookie { get; set; }

    /// <summary>Gets or sets the returned xattr names.</summary>
    public string[] Names { get; set; } = [];

    /// <summary>Gets or sets whether the end of the xattr list was reached.</summary>
    public bool Eof { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ListXattrs;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt64(Cookie);
            writer.WriteUInt32((uint)Names.Length);
            foreach (string name in Names)
            {
                writer.WriteString(name);
            }

            writer.WriteBool(Eof);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        Cookie = reader.ReadUInt64();
        uint count = reader.ReadUInt32();
        if (count > 4096)
        {
            throw new XdrException("LISTXATTRS name count is implausibly large.");
        }

        Names = new string[count];
        for (uint i = 0; i < count; i++)
        {
            Names[i] = reader.ReadString(Nfs4.MaxNameLength);
        }

        Eof = reader.ReadBool();
    }
}

/// <summary>The result of REMOVEXATTR.</summary>
public sealed class Nfs4RemoveXattrResult : Nfs4ResOp
{
    /// <summary>Gets or sets the object change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.RemoveXattr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            ChangeInfo.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            ChangeInfo = Nfs4ChangeInfo.ReadFrom(ref reader);
        }
    }
}
