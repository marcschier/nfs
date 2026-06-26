using System.Buffers;

using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>pNFS layout type identifiers (<c>layouttype4</c>, RFC 5661).</summary>
public enum Nfs4LayoutType : uint
{
    /// <summary>NFSv4.1 files layout (LAYOUT4_NFSV4_1_FILES).</summary>
    Files = 1,
}

/// <summary>pNFS layout I/O mode (<c>layoutiomode4</c>, RFC 5661).</summary>
public enum Nfs4LayoutIomode : uint
{
    /// <summary>Read-only layout (LAYOUTIOMODE4_READ).</summary>
    Read = 1,

    /// <summary>Read/write layout (LAYOUTIOMODE4_RW).</summary>
    ReadWrite = 2,
}

/// <summary>pNFS layout return selector (<c>layoutreturn_type4</c>, RFC 5661).</summary>
public enum Nfs4LayoutReturnType : uint
{
    /// <summary>Return a file layout (LAYOUTRETURN4_FILE).</summary>
    File = 1,
}

/// <summary>pNFS constants used by the minimal files-layout implementation.</summary>
public static class Nfs4Pnfs
{
    /// <summary>The fixed size of a pNFS device id (<c>NFS4_DEVICEID4_SIZE</c>).</summary>
    public const int DeviceIdSize = 16;

    /// <summary>The pNFS files-layout utility bit saying dense striping is used.</summary>
    public const uint FileLayoutUtilDense = 1;

    /// <summary>The files-layout utility bits reserved for flags; the remaining bits carry the stripe unit.</summary>
    public const uint FileLayoutUtilFlagMask = 0x3Fu;

    /// <summary>The default files-layout stripe unit, in bytes.</summary>
    public const uint DefaultStripeUnit = 65536;

    /// <summary>The default data-server device id used by the loopback files layout.</summary>
    public static byte[] DefaultDeviceId { get; } =
        [0x4e, 0x46, 0x53, 0x70, 0x4e, 0x46, 0x53, 0x44, 0, 0, 0, 0, 0, 0, 0, 1];
}

/// <summary>A pNFS network address (<c>netaddr4</c>).</summary>
public sealed class Nfs4NetAddress
{
    /// <summary>Gets or sets the RPC netid, for example <c>tcp</c>.</summary>
    public string NetId { get; set; } = "tcp";

    /// <summary>Gets or sets the universal address.</summary>
    public string Uaddr { get; set; } = string.Empty;

    /// <summary>Writes the address.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteString(NetId);
        writer.WriteString(Uaddr);
    }

    /// <summary>Reads an address.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded address.</returns>
    public static Nfs4NetAddress ReadFrom(ref XdrReader reader) => new()
    {
        NetId = reader.ReadString(1024),
        Uaddr = reader.ReadString(1024),
    };
}

/// <summary>A files-layout data-server address body (<c>nfsv4_1_file_layout_ds_addr4</c>).</summary>
public sealed class Nfs4FileLayoutDataServerAddress
{
    /// <summary>Gets or sets the stripe indices.</summary>
    public uint[] StripeIndices { get; set; } = [];

    /// <summary>Gets or sets the multipath data-server address list.</summary>
    public Nfs4NetAddress[][] MultipathDataServers { get; set; } = [];

    /// <summary>Writes the body.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)StripeIndices.Length);
        foreach (uint index in StripeIndices)
        {
            writer.WriteUInt32(index);
        }

        writer.WriteUInt32((uint)MultipathDataServers.Length);
        foreach (Nfs4NetAddress[] path in MultipathDataServers)
        {
            writer.WriteUInt32((uint)path.Length);
            foreach (Nfs4NetAddress address in path)
            {
                address.WriteTo(ref writer);
            }
        }
    }

    /// <summary>Reads the body.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded address body.</returns>
    public static Nfs4FileLayoutDataServerAddress ReadFrom(ref XdrReader reader)
    {
        uint stripeCount = ReadBoundedCount(ref reader);
        uint[] stripeIndices = new uint[stripeCount];
        for (int i = 0; i < stripeIndices.Length; i++)
        {
            stripeIndices[i] = reader.ReadUInt32();
        }

        uint serverCount = ReadBoundedCount(ref reader);
        Nfs4NetAddress[][] dataServers = new Nfs4NetAddress[serverCount][];
        for (int i = 0; i < dataServers.Length; i++)
        {
            uint pathCount = ReadBoundedCount(ref reader);
            dataServers[i] = new Nfs4NetAddress[pathCount];
            for (int j = 0; j < dataServers[i].Length; j++)
            {
                dataServers[i][j] = Nfs4NetAddress.ReadFrom(ref reader);
            }
        }

        return new Nfs4FileLayoutDataServerAddress
        {
            StripeIndices = stripeIndices,
            MultipathDataServers = dataServers,
        };
    }

    /// <summary>Reads a files-layout data-server address from a device address body.</summary>
    /// <param name="body">The opaque device address body.</param>
    /// <returns>The decoded body.</returns>
    public static Nfs4FileLayoutDataServerAddress Decode(ReadOnlyMemory<byte> body)
    {
        var reader = new XdrReader(body.Span);
        return ReadFrom(ref reader);
    }

    /// <summary>Encodes a files-layout data-server address as an opaque body.</summary>
    /// <returns>The encoded body.</returns>
    public byte[] Encode()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }

    private static uint ReadBoundedCount(ref XdrReader reader)
    {
        uint count = reader.ReadUInt32();
        if (count > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("Files-layout address list count is implausibly large.");
        }

        return count;
    }
}

/// <summary>A pNFS device address (<c>device_addr4</c>).</summary>
public sealed class Nfs4DeviceAddress
{
    /// <summary>Gets or sets the layout type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the layout-type-specific opaque address body.</summary>
    public byte[] Body { get; set; } = [];

    /// <summary>Writes the address.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)LayoutType);
        writer.WriteOpaqueVariable(Body);
    }

    /// <summary>Reads an address.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded address.</returns>
    public static Nfs4DeviceAddress ReadFrom(ref XdrReader reader) => new()
    {
        LayoutType = (Nfs4LayoutType)reader.ReadUInt32(),
        Body = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray(),
    };
}

/// <summary>A pNFS layout content body (<c>layout_content4</c>).</summary>
public sealed class Nfs4LayoutContent
{
    /// <summary>Gets or sets the layout type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the layout-type-specific opaque body.</summary>
    public byte[] Body { get; set; } = [];

    /// <summary>Writes the content.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)LayoutType);
        writer.WriteOpaqueVariable(Body);
    }

    /// <summary>Reads the content.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded content.</returns>
    public static Nfs4LayoutContent ReadFrom(ref XdrReader reader) => new()
    {
        LayoutType = (Nfs4LayoutType)reader.ReadUInt32(),
        Body = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray(),
    };
}

/// <summary>A single pNFS layout segment (<c>layout4</c>).</summary>
public sealed class Nfs4Layout
{
    /// <summary>Gets or sets the segment offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the segment length.</summary>
    public ulong Length { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets the I/O mode.</summary>
    public Nfs4LayoutIomode Iomode { get; set; } = Nfs4LayoutIomode.ReadWrite;

    /// <summary>Gets or sets the layout content.</summary>
    public Nfs4LayoutContent Content { get; set; } = new();

    /// <summary>Writes the layout.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteUInt32((uint)Iomode);
        Content.WriteTo(ref writer);
    }

    /// <summary>Reads the layout.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded layout.</returns>
    public static Nfs4Layout ReadFrom(ref XdrReader reader) => new()
    {
        Offset = reader.ReadUInt64(),
        Length = reader.ReadUInt64(),
        Iomode = (Nfs4LayoutIomode)reader.ReadUInt32(),
        Content = Nfs4LayoutContent.ReadFrom(ref reader),
    };
}

/// <summary>A files-layout body (<c>nfsv4_1_file_layout4</c>).</summary>
public sealed class Nfs4FileLayout
{
    /// <summary>Gets or sets the device id.</summary>
    public byte[] DeviceId { get; set; } = new byte[Nfs4Pnfs.DeviceIdSize];

    /// <summary>Gets or sets the files-layout utility flags.</summary>
    public uint Util { get; set; } = Nfs4Pnfs.FileLayoutUtilDense;

    /// <summary>Gets or sets the files-layout stripe unit carried in <see cref="Util"/>.</summary>
    public uint StripeUnit
    {
        get => Util & ~Nfs4Pnfs.FileLayoutUtilFlagMask;
        set => Util = (Util & Nfs4Pnfs.FileLayoutUtilFlagMask) | (value & ~Nfs4Pnfs.FileLayoutUtilFlagMask);
    }

    /// <summary>Gets or sets the first stripe index.</summary>
    public uint FirstStripeIndex { get; set; }

    /// <summary>Gets or sets the pattern offset.</summary>
    public ulong PatternOffset { get; set; }

    /// <summary>Gets or sets the data-server file handles.</summary>
    public Nfs4Handle[] FileHandles { get; set; } = [];

    /// <summary>Writes the layout body.</summary>
    /// <param name="writer">The writer.</param>
    public void WriteTo(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(DeviceId);
        writer.WriteUInt32(Util);
        writer.WriteUInt32(FirstStripeIndex);
        writer.WriteUInt64(PatternOffset);
        writer.WriteUInt32((uint)FileHandles.Length);
        foreach (Nfs4Handle handle in FileHandles)
        {
            handle.WriteTo(ref writer);
        }
    }

    /// <summary>Reads the layout body.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>The decoded layout body.</returns>
    public static Nfs4FileLayout ReadFrom(ref XdrReader reader)
    {
        byte[] deviceId = reader.ReadOpaqueFixed(Nfs4Pnfs.DeviceIdSize).ToArray();
        uint util = reader.ReadUInt32();
        uint firstStripeIndex = reader.ReadUInt32();
        ulong patternOffset = reader.ReadUInt64();
        uint handleCount = reader.ReadUInt32();
        if (handleCount > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("Files-layout file-handle count is implausibly large.");
        }

        Nfs4Handle[] handles = new Nfs4Handle[handleCount];
        for (int i = 0; i < handles.Length; i++)
        {
            handles[i] = Nfs4Handle.ReadFrom(ref reader);
        }

        return new Nfs4FileLayout
        {
            DeviceId = deviceId,
            Util = util,
            FirstStripeIndex = firstStripeIndex,
            PatternOffset = patternOffset,
            FileHandles = handles,
        };
    }

    /// <summary>Reads a files-layout body from opaque layout content.</summary>
    /// <param name="body">The opaque layout body.</param>
    /// <returns>The decoded body.</returns>
    public static Nfs4FileLayout Decode(ReadOnlyMemory<byte> body)
    {
        var reader = new XdrReader(body.Span);
        return ReadFrom(ref reader);
    }

    /// <summary>Encodes this files layout as an opaque layout body.</summary>
    /// <returns>The encoded body.</returns>
    public byte[] Encode()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(buffer);
        WriteTo(ref writer);
        return buffer.WrittenSpan.ToArray();
    }
}

/// <summary>GETDEVICEINFO: get a pNFS device address.</summary>
public sealed class Nfs4GetDeviceInfoOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the 16-byte device id.</summary>
    public byte[] DeviceId { get; set; } = Nfs4Pnfs.DefaultDeviceId.ToArray();

    /// <summary>Gets or sets the requested layout type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the maximum number of bytes to return.</summary>
    public uint MaxCount { get; set; } = Nfs4.MaxIoSize;

    /// <summary>Gets or sets the notification bitmap.</summary>
    public Nfs4Bitmap NotifyTypes { get; set; } = Nfs4Bitmap.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetDeviceInfo;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteOpaqueFixed(DeviceId);
        writer.WriteUInt32((uint)LayoutType);
        writer.WriteUInt32(MaxCount);
        NotifyTypes.WriteTo(ref writer);
    }
}

/// <summary>The result of GETDEVICEINFO.</summary>
public sealed class Nfs4GetDeviceInfoResult : Nfs4ResOp
{
    /// <summary>Gets or sets the returned device address.</summary>
    public Nfs4DeviceAddress DeviceAddress { get; set; } = new();

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetDeviceInfo;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            DeviceAddress.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            DeviceAddress = Nfs4DeviceAddress.ReadFrom(ref reader);
        }
    }
}

/// <summary>LAYOUTGET: obtain a pNFS layout segment.</summary>
public sealed class Nfs4LayoutGetOp : Nfs4ArgOp
{
    /// <summary>Gets or sets whether the server should signal when a layout becomes available.</summary>
    public bool SignalLayoutAvailable { get; set; }

    /// <summary>Gets or sets the requested layout type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the requested I/O mode.</summary>
    public Nfs4LayoutIomode Iomode { get; set; } = Nfs4LayoutIomode.ReadWrite;

    /// <summary>Gets or sets the requested byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the requested byte length.</summary>
    public ulong Length { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets the minimum byte length.</summary>
    public ulong MinLength { get; set; }

    /// <summary>Gets or sets the state id.</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the maximum number of bytes to return.</summary>
    public uint MaxCount { get; set; } = Nfs4.MaxIoSize;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutGet;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteBool(SignalLayoutAvailable);
        writer.WriteUInt32((uint)LayoutType);
        writer.WriteUInt32((uint)Iomode);
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteUInt64(MinLength);
        StateId.WriteTo(ref writer);
        writer.WriteUInt32(MaxCount);
    }
}

/// <summary>The result of LAYOUTGET.</summary>
public sealed class Nfs4LayoutGetResult : Nfs4ResOp
{
    /// <summary>Gets or sets whether the layout should be returned on close.</summary>
    public bool ReturnOnClose { get; set; }

    /// <summary>Gets or sets the returned state id.</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the returned layouts.</summary>
    public Nfs4Layout[] Layouts { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutGet;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(ReturnOnClose);
            StateId.WriteTo(ref writer);
            writer.WriteUInt32((uint)Layouts.Length);
            foreach (Nfs4Layout layout in Layouts)
            {
                layout.WriteTo(ref writer);
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

        ReturnOnClose = reader.ReadBool();
        StateId = Nfs4StateId.ReadFrom(ref reader);
        uint count = reader.ReadUInt32();
        if (count > Nfs4CompoundArgs.MaxOperations)
        {
            throw new XdrException("LAYOUTGET layout count is implausibly large.");
        }

        Layouts = new Nfs4Layout[count];
        for (int i = 0; i < Layouts.Length; i++)
        {
            Layouts[i] = Nfs4Layout.ReadFrom(ref reader);
        }
    }
}

/// <summary>LAYOUTCOMMIT: commit layout changes.</summary>
public sealed class Nfs4LayoutCommitOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the committed offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the committed length.</summary>
    public ulong Length { get; set; }

    /// <summary>Gets or sets whether this is reclaiming previous layout state.</summary>
    public bool Reclaim { get; set; }

    /// <summary>Gets or sets the state id.</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the optional new end offset.</summary>
    public ulong? NewOffset { get; set; }

    /// <summary>Gets or sets the optional new modification time.</summary>
    public Nfs4Time? NewTime { get; set; }

    /// <summary>Gets or sets the layout-update type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the layout-update body.</summary>
    public byte[] LayoutUpdate { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutCommit;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt64(Offset);
        writer.WriteUInt64(Length);
        writer.WriteBool(Reclaim);
        StateId.WriteTo(ref writer);
        writer.WriteBool(NewOffset.HasValue);
        if (NewOffset.HasValue)
        {
            writer.WriteUInt64(NewOffset.GetValueOrDefault());
        }

        writer.WriteBool(NewTime.HasValue);
        if (NewTime.HasValue)
        {
            NewTime.GetValueOrDefault().WriteTo(ref writer);
        }

        writer.WriteUInt32((uint)LayoutType);
        writer.WriteOpaqueVariable(LayoutUpdate);
    }
}

/// <summary>The result of LAYOUTCOMMIT.</summary>
public sealed class Nfs4LayoutCommitResult : Nfs4ResOp
{
    /// <summary>Gets or sets the optional new file size.</summary>
    public ulong? NewSize { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutCommit;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(NewSize.HasValue);
            if (NewSize.HasValue)
            {
                writer.WriteUInt64(NewSize.GetValueOrDefault());
            }
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess && reader.ReadBool())
        {
            NewSize = reader.ReadUInt64();
        }
    }
}

/// <summary>LAYOUTRETURN: return layout state.</summary>
public sealed class Nfs4LayoutReturnOp : Nfs4ArgOp
{
    /// <summary>Gets or sets whether this is reclaiming previous layout state.</summary>
    public bool Reclaim { get; set; }

    /// <summary>Gets or sets the layout type.</summary>
    public Nfs4LayoutType LayoutType { get; set; } = Nfs4LayoutType.Files;

    /// <summary>Gets or sets the layout I/O mode.</summary>
    public Nfs4LayoutIomode Iomode { get; set; } = Nfs4LayoutIomode.ReadWrite;

    /// <summary>Gets or sets the layout return type.</summary>
    public Nfs4LayoutReturnType ReturnType { get; set; } = Nfs4LayoutReturnType.File;

    /// <summary>Gets or sets the file-layout offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the file-layout length.</summary>
    public ulong Length { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets the state id.</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the layout-type-specific return body.</summary>
    public byte[] Body { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutReturn;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteBool(Reclaim);
        writer.WriteUInt32((uint)LayoutType);
        writer.WriteUInt32((uint)Iomode);
        writer.WriteUInt32((uint)ReturnType);
        if (ReturnType == Nfs4LayoutReturnType.File)
        {
            writer.WriteUInt64(Offset);
            writer.WriteUInt64(Length);
            StateId.WriteTo(ref writer);
            writer.WriteOpaqueVariable(Body);
        }
    }
}

/// <summary>The result of LAYOUTRETURN.</summary>
public sealed class Nfs4LayoutReturnResult : Nfs4ResOp
{
    /// <summary>Gets or sets the optional returned state id.</summary>
    public Nfs4StateId? StateId { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LayoutReturn;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(StateId.HasValue);
            if (StateId.HasValue)
            {
                StateId.GetValueOrDefault().WriteTo(ref writer);
            }
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess && reader.ReadBool())
        {
            StateId = Nfs4StateId.ReadFrom(ref reader);
        }
    }
}
