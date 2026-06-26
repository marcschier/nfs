using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>Information about a directory change (<c>change_info4</c>, RFC 7530).</summary>
public record struct Nfs4ChangeInfo : IXdrSerializable<Nfs4ChangeInfo>
{
    /// <summary>Whether the before/after change values were captured atomically with the operation.</summary>
    public bool Atomic { get; set; }

    /// <summary>The directory change value before the operation.</summary>
    public ulong Before { get; set; }

    /// <summary>The directory change value after the operation.</summary>
    public ulong After { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        writer.WriteBool(Atomic);
        writer.WriteUInt64(Before);
        writer.WriteUInt64(After);
    }

    /// <inheritdoc/>
    public static Nfs4ChangeInfo ReadFrom(ref XdrReader reader) => new()
    {
        Atomic = reader.ReadBool(),
        Before = reader.ReadUInt64(),
        After = reader.ReadUInt64(),
    };
}

/// <summary>
/// The base type for a decoded NFS version 4 result operation inside a COMPOUND
/// (<c>nfs_resop4</c>, RFC 7530). Each subclass encodes and decodes its own status and result data.
/// </summary>
public abstract class Nfs4ResOp
{
    /// <summary>Gets the operation code.</summary>
    public abstract Nfs4Op Op { get; }

    /// <summary>Gets or sets the operation status.</summary>
    public Nfs4Status Status { get; set; }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess => Status == Nfs4Status.Ok;

    /// <summary>Encodes the operation's result (excluding the leading opcode).</summary>
    /// <param name="writer">The writer.</param>
    public abstract void Encode(ref XdrWriter writer);

    /// <summary>Reads the operation's result fields beyond the status (which is already read).</summary>
    /// <param name="reader">The reader.</param>
    protected abstract void DecodeResok(ref XdrReader reader);

    /// <summary>Reads the status and, on success, the result fields.</summary>
    /// <param name="reader">The reader.</param>
    public void Decode(ref XdrReader reader)
    {
        Status = (Nfs4Status)reader.ReadInt32();
        DecodeResok(ref reader);
    }
}

/// <summary>A result that carries only a status (PUTROOTFH, PUTFH, SAVEFH, RESTOREFH, LOOKUP, LOOKUPP).</summary>
public sealed class Nfs4StatusResult : Nfs4ResOp
{
    /// <summary>Creates a status-only result for <paramref name="op"/>.</summary>
    /// <param name="op">The operation code.</param>
    public Nfs4StatusResult(Nfs4Op op) => Op = op;

    /// <inheritdoc/>
    public override Nfs4Op Op { get; }

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteInt32((int)Status);

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
    }
}

/// <summary>The result of GETFH (the current file handle on success).</summary>
public sealed class Nfs4GetFhResult : Nfs4ResOp
{
    /// <summary>Gets or sets the returned handle.</summary>
    public Nfs4Handle Handle { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            Handle.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Handle = Nfs4Handle.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of GETATTR (the requested attributes on success).</summary>
public sealed class Nfs4GetAttrResult : Nfs4ResOp
{
    /// <summary>Gets or sets the returned attributes.</summary>
    public Nfs4FAttr Attributes { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetAttr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            Attributes.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Attributes = Nfs4FAttr.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of ACCESS (the supported and granted access bits on success).</summary>
public sealed class Nfs4AccessResult : Nfs4ResOp
{
    /// <summary>Gets or sets the bits the server can evaluate.</summary>
    public uint Supported { get; set; }

    /// <summary>Gets or sets the bits that are granted.</summary>
    public uint Access { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Access;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt32(Supported);
            writer.WriteUInt32(Access);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Supported = reader.ReadUInt32();
            Access = reader.ReadUInt32();
        }
    }
}

/// <summary>The result of COMMIT (the server write verifier on success).</summary>
public sealed class Nfs4CommitResult : Nfs4ResOp
{
    /// <summary>Gets or sets the write verifier (8 bytes).</summary>
    public byte[] Verifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Commit;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteOpaqueFixed(Verifier);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Verifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray();
        }
    }
}

/// <summary>The result of SECINFO and SECINFO_NO_NAME.</summary>
public sealed class Nfs4SecInfoResult : Nfs4ResOp
{
    /// <summary>Creates a SECINFO result for <paramref name="op"/>.</summary>
    /// <param name="op">The operation code.</param>
    public Nfs4SecInfoResult(Nfs4Op op) => Op = op;

    /// <inheritdoc/>
    public override Nfs4Op Op { get; }

    /// <summary>Gets or sets the returned security flavor entries.</summary>
    public Nfs4SecInfo[] Flavors { get; set; } = [];

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        writer.WriteUInt32((uint)Flavors.Length);
        foreach (Nfs4SecInfo flavor in Flavors)
        {
            flavor.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        uint count = reader.ReadUInt32();
        if (count > 64)
        {
            throw new XdrException("SECINFO result count is implausibly large.");
        }

        var flavors = new Nfs4SecInfo[(int)count];
        for (int i = 0; i < flavors.Length; i++)
        {
            flavors[i] = Nfs4SecInfo.ReadFrom(ref reader);
        }

        Flavors = flavors;
    }
}

/// <summary>The result of READ (the data and end-of-file flag on success).</summary>
public sealed class Nfs4ReadResult : Nfs4ResOp
{
    /// <summary>Gets or sets whether the end of the file was reached.</summary>
    public bool Eof { get; set; }

    /// <summary>Gets or sets the data read.</summary>
    public byte[] Data { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Read;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteBool(Eof);
            writer.WriteOpaqueVariable(Data);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Eof = reader.ReadBool();
            Data = reader.ReadOpaqueVariable(Nfs4.MaxIoSize).ToArray();
        }
    }
}

/// <summary>The result of WRITE (the byte count, commit level, and verifier on success).</summary>
public sealed class Nfs4WriteResult : Nfs4ResOp
{
    /// <summary>Gets or sets the number of bytes written.</summary>
    public uint Count { get; set; }

    /// <summary>Gets or sets how durably the data was committed.</summary>
    public uint Committed { get; set; }

    /// <summary>Gets or sets the write verifier (8 bytes).</summary>
    public byte[] Verifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Write;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteUInt32(Count);
            writer.WriteUInt32(Committed);
            writer.WriteOpaqueFixed(Verifier);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Count = reader.ReadUInt32();
            Committed = reader.ReadUInt32();
            Verifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray();
        }
    }
}

/// <summary>The result of READLINK (the link target on success).</summary>
public sealed class Nfs4ReadLinkResult : Nfs4ResOp
{
    /// <summary>Gets or sets the link target path.</summary>
    public string Target { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadLink;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            writer.WriteString(Target);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Target = reader.ReadString(Nfs4.MaxIoSize);
        }
    }
}

/// <summary>A single READDIR entry (<c>entry4</c>, RFC 7530).</summary>
/// <param name="Cookie">The continuation cookie positioned after this entry.</param>
/// <param name="Name">The entry name.</param>
/// <param name="Attributes">The requested attributes for the entry.</param>
public readonly record struct Nfs4DirEntry(ulong Cookie, string Name, Nfs4FAttr Attributes);

/// <summary>The result of READDIR (the entries and end-of-file flag on success).</summary>
public sealed class Nfs4ReadDirResult : Nfs4ResOp
{
    /// <summary>Gets or sets the cookie verifier (8 bytes).</summary>
    public byte[] CookieVerifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Gets or sets the directory entries.</summary>
    public Nfs4DirEntry[] Entries { get; set; } = [];

    /// <summary>Gets or sets whether the end of the directory was reached.</summary>
    public bool Eof { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadDir;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (!IsSuccess)
        {
            return;
        }

        writer.WriteOpaqueFixed(CookieVerifier);
        foreach (Nfs4DirEntry entry in Entries)
        {
            writer.WriteBool(true);
            writer.WriteUInt64(entry.Cookie);
            writer.WriteString(entry.Name);
            entry.Attributes.WriteTo(ref writer);
        }

        writer.WriteBool(false);
        writer.WriteBool(Eof);
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (!IsSuccess)
        {
            return;
        }

        CookieVerifier = reader.ReadOpaqueFixed(Nfs4.VerifierSize).ToArray();
        var entries = new List<Nfs4DirEntry>();
        while (reader.ReadBool())
        {
            ulong cookie = reader.ReadUInt64();
            string name = reader.ReadString(Nfs4.MaxNameLength);
            Nfs4FAttr attributes = Nfs4FAttr.ReadFrom(ref reader);
            entries.Add(new Nfs4DirEntry(cookie, name, attributes));
        }

        Eof = reader.ReadBool();
        Entries = [.. entries];
    }
}

/// <summary>The result of REMOVE (directory change information on success).</summary>
public sealed class Nfs4RemoveResult : Nfs4ResOp
{
    /// <summary>Gets or sets the directory change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Remove;

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

/// <summary>The result of RENAME (source and target directory change information on success).</summary>
public sealed class Nfs4RenameResult : Nfs4ResOp
{
    /// <summary>Gets or sets the source directory change information.</summary>
    public Nfs4ChangeInfo Source { get; set; }

    /// <summary>Gets or sets the target directory change information.</summary>
    public Nfs4ChangeInfo Target { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Rename;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            Source.WriteTo(ref writer);
            Target.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            Source = Nfs4ChangeInfo.ReadFrom(ref reader);
            Target = Nfs4ChangeInfo.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of LINK (directory change information on success).</summary>
public sealed class Nfs4LinkResult : Nfs4ResOp
{
    /// <summary>Gets or sets the target directory change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Link;

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

/// <summary>The result of CREATE (directory change information and the attribute set on success).</summary>
public sealed class Nfs4CreateResult : Nfs4ResOp
{
    /// <summary>Gets or sets the directory change information.</summary>
    public Nfs4ChangeInfo ChangeInfo { get; set; }

    /// <summary>Gets or sets the bitmap of attributes that were set.</summary>
    public Nfs4Bitmap AttributesSet { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Create;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        if (IsSuccess)
        {
            ChangeInfo.WriteTo(ref writer);
            AttributesSet.WriteTo(ref writer);
        }
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader)
    {
        if (IsSuccess)
        {
            ChangeInfo = Nfs4ChangeInfo.ReadFrom(ref reader);
            AttributesSet = Nfs4Bitmap.ReadFrom(ref reader);
        }
    }
}

/// <summary>The result of SETATTR (the bitmap of attributes that were set; always present).</summary>
public sealed class Nfs4SetAttrResult : Nfs4ResOp
{
    /// <summary>Gets or sets the bitmap of attributes that were set.</summary>
    public Nfs4Bitmap AttributesSet { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetAttr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteInt32((int)Status);
        AttributesSet.WriteTo(ref writer);
    }

    /// <inheritdoc/>
    protected override void DecodeResok(ref XdrReader reader) => AttributesSet = Nfs4Bitmap.ReadFrom(ref reader);
}
