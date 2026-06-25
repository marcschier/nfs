using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>
/// The base type for a decoded NFS version 4 request operation inside a COMPOUND
/// (<c>nfs_argop4</c>, RFC 7530). Each subclass encodes and decodes its own arguments.
/// </summary>
public abstract class Nfs4ArgOp
{
    /// <summary>Gets the operation code.</summary>
    public abstract Nfs4Op Op { get; }

    /// <summary>Encodes the operation's arguments (excluding the leading opcode).</summary>
    /// <param name="writer">The writer.</param>
    public abstract void Encode(ref XdrWriter writer);
}

/// <summary>PUTROOTFH: set the current file handle to the export root.</summary>
public sealed class Nfs4PutRootFhOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.PutRootFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>GETFH: return the current file handle.</summary>
public sealed class Nfs4GetFhOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>SAVEFH: copy the current file handle to the saved handle.</summary>
public sealed class Nfs4SaveFhOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SaveFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>RESTOREFH: set the current file handle from the saved handle.</summary>
public sealed class Nfs4RestoreFhOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.RestoreFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>LOOKUPP: set the current file handle to the parent directory.</summary>
public sealed class Nfs4LookupParentOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.LookupParent;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>READLINK: read the current symbolic link's target.</summary>
public sealed class Nfs4ReadLinkOp : Nfs4ArgOp
{
    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadLink;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
    }
}

/// <summary>PUTFH: set the current file handle to a specified handle.</summary>
public sealed class Nfs4PutFhOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the handle to make current.</summary>
    public Nfs4Handle Handle { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.PutFh;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => Handle.WriteTo(ref writer);
}

/// <summary>LOOKUP: set the current file handle to a named child of the current directory.</summary>
public sealed class Nfs4LookupOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the component name to resolve.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Lookup;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteString(Name);
}

/// <summary>SECINFO: return the security flavors available for a named child.</summary>
public sealed class Nfs4SecInfoOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the component name to query.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SecInfo;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteString(Name);
}

/// <summary>SECINFO_NO_NAME: return the security flavors for the current file handle or its parent.</summary>
public sealed class Nfs4SecInfoNoNameOp : Nfs4ArgOp
{
    /// <summary>Gets or sets which handle to query.</summary>
    public Nfs4SecInfoStyle Style { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SecInfoNoName;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteUInt32((uint)Style);
}

/// <summary>GETATTR: return the requested attributes of the current file handle.</summary>
public sealed class Nfs4GetAttrOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the requested attribute bitmap.</summary>
    public Nfs4Bitmap Request { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.GetAttr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => Request.WriteTo(ref writer);
}

/// <summary>ACCESS: check the caller's permissions on the current file handle.</summary>
public sealed class Nfs4AccessOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the access bits to check.</summary>
    public uint Access { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Access;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteUInt32(Access);
}

/// <summary>READ: read data from the current file handle.</summary>
public sealed class Nfs4ReadOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the read state identifier (anonymous for stateless reads).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets the byte count.</summary>
    public uint Count { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Read;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt32(Count);
    }
}

/// <summary>WRITE: write data to the current file handle.</summary>
public sealed class Nfs4WriteOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the write state identifier (anonymous for stateless writes).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the byte offset.</summary>
    public ulong Offset { get; set; }

    /// <summary>Gets or sets how durably the server must commit the data.</summary>
    public uint Stable { get; set; }

    /// <summary>Gets or sets the data to write.</summary>
    public byte[] Data { get; set; } = [];

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Write;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        writer.WriteUInt64(Offset);
        writer.WriteUInt32(Stable);
        writer.WriteOpaqueVariable(Data);
    }
}

/// <summary>READDIR: read entries from the current directory.</summary>
public sealed class Nfs4ReadDirOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the continuation cookie.</summary>
    public ulong Cookie { get; set; }

    /// <summary>Gets or sets the cookie verifier (8 bytes).</summary>
    public byte[] CookieVerifier { get; set; } = new byte[Nfs4.VerifierSize];

    /// <summary>Gets or sets the maximum directory-information bytes to return.</summary>
    public uint DirectoryCount { get; set; }

    /// <summary>Gets or sets the maximum total bytes to return.</summary>
    public uint MaxCount { get; set; }

    /// <summary>Gets or sets the requested per-entry attribute bitmap.</summary>
    public Nfs4Bitmap Request { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.ReadDir;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt64(Cookie);
        writer.WriteOpaqueFixed(CookieVerifier);
        writer.WriteUInt32(DirectoryCount);
        writer.WriteUInt32(MaxCount);
        Request.WriteTo(ref writer);
    }
}

/// <summary>REMOVE: remove a named entry from the current directory.</summary>
public sealed class Nfs4RemoveOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the entry name to remove.</summary>
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Remove;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer) => writer.WriteString(Name);
}

/// <summary>RENAME: rename from the saved directory to the current directory.</summary>
public sealed class Nfs4RenameOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the source name (in the saved-handle directory).</summary>
    public string OldName { get; set; } = string.Empty;

    /// <summary>Gets or sets the destination name (in the current-handle directory).</summary>
    public string NewName { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Rename;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteString(OldName);
        writer.WriteString(NewName);
    }
}

/// <summary>SETATTR: set attributes on the current file handle.</summary>
public sealed class Nfs4SetAttrOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the state identifier (anonymous when only metadata changes).</summary>
    public Nfs4StateId StateId { get; set; } = Nfs4StateId.Anonymous;

    /// <summary>Gets or sets the attributes to set.</summary>
    public Nfs4FAttr Attributes { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.SetAttr;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        StateId.WriteTo(ref writer);
        Attributes.WriteTo(ref writer);
    }
}

/// <summary>The kind of object an OP_CREATE produces (<c>createtype4</c>, RFC 7530).</summary>
public enum Nfs4CreateType
{
    /// <summary>A directory (NF4DIR).</summary>
    Directory = 2,

    /// <summary>A symbolic link (NF4LNK).</summary>
    SymbolicLink = 5,
}

/// <summary>CREATE: create a non-regular object (a directory or symbolic link) in the current directory.</summary>
public sealed class Nfs4CreateOp : Nfs4ArgOp
{
    /// <summary>Gets or sets the type of object to create.</summary>
    public Nfs4CreateType Type { get; set; }

    /// <summary>Gets or sets the symbolic-link target (only for <see cref="Nfs4CreateType.SymbolicLink"/>).</summary>
    public string LinkTarget { get; set; } = string.Empty;

    /// <summary>Gets or sets the new object's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the initial attributes.</summary>
    public Nfs4FAttr Attributes { get; set; }

    /// <inheritdoc/>
    public override Nfs4Op Op => Nfs4Op.Create;

    /// <inheritdoc/>
    public override void Encode(ref XdrWriter writer)
    {
        writer.WriteUInt32((uint)Type);
        if (Type == Nfs4CreateType.SymbolicLink)
        {
            writer.WriteString(LinkTarget);
        }

        writer.WriteString(Name);
        Attributes.WriteTo(ref writer);
    }
}
