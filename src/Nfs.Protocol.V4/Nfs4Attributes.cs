using System.Buffers;

using Nfs.Abstractions;

using Nfs.Xdr;

namespace Nfs.Protocol.V4;

/// <summary>The NFS version 4 attribute numbers this implementation understands (RFC 7530 §5).</summary>
public enum Nfs4AttributeId
{
    /// <summary>The set of attributes the server supports (FATTR4_SUPPORTED_ATTRS).</summary>
    SupportedAttributes = 0,

    /// <summary>The object type (FATTR4_TYPE).</summary>
    Type = 1,

    /// <summary>How file handles expire (FATTR4_FH_EXPIRE_TYPE).</summary>
    FileHandleExpireType = 2,

    /// <summary>The change attribute (FATTR4_CHANGE).</summary>
    Change = 3,

    /// <summary>The object size in bytes (FATTR4_SIZE).</summary>
    Size = 4,

    /// <summary>Whether hard links are supported (FATTR4_LINK_SUPPORT).</summary>
    LinkSupport = 5,

    /// <summary>Whether symbolic links are supported (FATTR4_SYMLINK_SUPPORT).</summary>
    SymlinkSupport = 6,

    /// <summary>Whether the object has named attributes (FATTR4_NAMED_ATTR).</summary>
    NamedAttribute = 7,

    /// <summary>The file-system identifier (FATTR4_FSID).</summary>
    FileSystemId = 8,

    /// <summary>Whether handles are unique across the server (FATTR4_UNIQUE_HANDLES).</summary>
    UniqueHandles = 9,

    /// <summary>The lease time in seconds (FATTR4_LEASE_TIME).</summary>
    LeaseTime = 10,

    /// <summary>The error to report for a per-object attribute read (FATTR4_RDATTR_ERROR).</summary>
    ReadAttributeError = 11,

    /// <summary>The object's NFSv4 access control list (FATTR4_ACL).</summary>
    Acl = 12,

    /// <summary>The supported NFSv4 ACL entry types (FATTR4_ACLSUPPORT).</summary>
    AclSupport = 13,

    /// <summary>The object's file handle (FATTR4_FILEHANDLE).</summary>
    FileHandle = 19,

    /// <summary>The object's unique identifier (FATTR4_FILEID).</summary>
    FileId = 20,

    /// <summary>The maximum file size (FATTR4_MAXFILESIZE).</summary>
    MaxFileSize = 27,

    /// <summary>The maximum number of hard links (FATTR4_MAXLINK).</summary>
    MaxLink = 28,

    /// <summary>The maximum file-name length (FATTR4_MAXNAME).</summary>
    MaxName = 29,

    /// <summary>The maximum READ size (FATTR4_MAXREAD).</summary>
    MaxRead = 30,

    /// <summary>The maximum WRITE size (FATTR4_MAXWRITE).</summary>
    MaxWrite = 31,

    /// <summary>The protection mode bits (FATTR4_MODE).</summary>
    Mode = 33,

    /// <summary>The number of hard links (FATTR4_NUMLINKS).</summary>
    NumLinks = 35,

    /// <summary>The owner, as a string (FATTR4_OWNER).</summary>
    Owner = 36,

    /// <summary>The owning group, as a string (FATTR4_OWNER_GROUP).</summary>
    OwnerGroup = 37,

    /// <summary>The device numbers, for special files (FATTR4_RAWDEV).</summary>
    RawDevice = 41,

    /// <summary>The bytes of storage consumed (FATTR4_SPACE_USED).</summary>
    SpaceUsed = 45,

    /// <summary>The last access time (FATTR4_TIME_ACCESS).</summary>
    TimeAccess = 47,

    /// <summary>The last metadata-change time (FATTR4_TIME_METADATA).</summary>
    TimeMetadata = 52,

    /// <summary>The last data-modification time (FATTR4_TIME_MODIFY).</summary>
    TimeModify = 53,

    /// <summary>Whether RFC 8276 extended attributes are supported (FATTR4_XATTR_SUPPORT).</summary>
    XattrSupport = 82,
}

/// <summary>
/// A set of NFS version 4 object attributes (the encoded <c>fattr4</c> structure, RFC 7530). Each
/// property is optional; only present attributes are encoded, and only requested attributes are read.
/// </summary>
public readonly record struct Nfs4FileAttributes
{
    /// <summary>The set of attributes the server supports.</summary>
    public Nfs4Bitmap? SupportedAttributes { get; init; }

    /// <summary>The object type.</summary>
    public Nfs4FileType? Type { get; init; }

    /// <summary>How file handles expire (0 = persistent).</summary>
    public uint? FileHandleExpireType { get; init; }

    /// <summary>The change attribute (bumped on every modification).</summary>
    public ulong? Change { get; init; }

    /// <summary>The object size in bytes.</summary>
    public ulong? Size { get; init; }

    /// <summary>Whether hard links are supported.</summary>
    public bool? LinkSupport { get; init; }

    /// <summary>Whether symbolic links are supported.</summary>
    public bool? SymlinkSupport { get; init; }

    /// <summary>Whether the object has named attributes.</summary>
    public bool? NamedAttribute { get; init; }

    /// <summary>The file-system identifier.</summary>
    public Nfs4Fsid? FileSystemId { get; init; }

    /// <summary>Whether handles are unique across the server.</summary>
    public bool? UniqueHandles { get; init; }

    /// <summary>The lease time in seconds.</summary>
    public uint? LeaseTime { get; init; }

    /// <summary>The error to report for a per-object attribute read.</summary>
    public Nfs4Status? ReadAttributeError { get; init; }

    /// <summary>The object's file handle.</summary>
    public byte[]? FileHandle { get; init; }

    /// <summary>The object's unique identifier within the file system.</summary>
    public ulong? FileId { get; init; }

    /// <summary>The maximum file size.</summary>
    public ulong? MaxFileSize { get; init; }

    /// <summary>The maximum number of hard links.</summary>
    public uint? MaxLink { get; init; }

    /// <summary>The maximum file-name length.</summary>
    public uint? MaxName { get; init; }

    /// <summary>The maximum READ size.</summary>
    public ulong? MaxRead { get; init; }

    /// <summary>The maximum WRITE size.</summary>
    public ulong? MaxWrite { get; init; }

    /// <summary>The protection mode bits.</summary>
    public uint? Mode { get; init; }

    /// <summary>The number of hard links.</summary>
    public uint? NumLinks { get; init; }

    /// <summary>The owner, as a string.</summary>
    public string? Owner { get; init; }

    /// <summary>The owning group, as a string.</summary>
    public string? OwnerGroup { get; init; }

    /// <summary>The device numbers, for special files.</summary>
    public Nfs4SpecData? RawDevice { get; init; }

    /// <summary>The bytes of storage consumed.</summary>
    public ulong? SpaceUsed { get; init; }

    /// <summary>The last access time.</summary>
    public Nfs4Time? TimeAccess { get; init; }

    /// <summary>The last metadata-change time.</summary>
    public Nfs4Time? TimeMetadata { get; init; }

    /// <summary>The last data-modification time.</summary>
    public Nfs4Time? TimeModify { get; init; }

    /// <summary>The NFSv4 access control list.</summary>
    public IReadOnlyList<NfsAccessControlEntry>? AccessControlList { get; init; }

    /// <summary>The supported NFSv4 ACL entry types.</summary>
    public uint? AclSupport { get; init; }

    /// <summary>Whether RFC 8276 extended attributes are supported.</summary>
    public bool? XattrSupport { get; init; }

    /// <summary>Encodes the attributes selected by <paramref name="requested"/> into an <c>fattr4</c>.</summary>
    /// <param name="requested">The attributes the caller asked for.</param>
    /// <returns>The encoded attribute set (mask of attributes actually present plus their values).</returns>
    public Nfs4FAttr Encode(Nfs4Bitmap requested)
    {
        var values = new ArrayBufferWriter<byte>();
        var writer = new XdrWriter(values);
        uint word0 = 0;
        uint word1 = 0;
        uint word2 = 0;

        if (Want(requested, Nfs4AttributeId.SupportedAttributes, SupportedAttributes.HasValue, ref word0))
        {
            SupportedAttributes!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.Type, Type.HasValue, ref word0))
        {
            writer.WriteUInt32((uint)Type!.Value);
        }

        if (Want(requested, Nfs4AttributeId.FileHandleExpireType, FileHandleExpireType.HasValue, ref word0))
        {
            writer.WriteUInt32(FileHandleExpireType!.Value);
        }

        if (Want(requested, Nfs4AttributeId.Change, Change.HasValue, ref word0))
        {
            writer.WriteUInt64(Change!.Value);
        }

        if (Want(requested, Nfs4AttributeId.Size, Size.HasValue, ref word0))
        {
            writer.WriteUInt64(Size!.Value);
        }

        if (Want(requested, Nfs4AttributeId.LinkSupport, LinkSupport.HasValue, ref word0))
        {
            writer.WriteBool(LinkSupport!.Value);
        }

        if (Want(requested, Nfs4AttributeId.SymlinkSupport, SymlinkSupport.HasValue, ref word0))
        {
            writer.WriteBool(SymlinkSupport!.Value);
        }

        if (Want(requested, Nfs4AttributeId.NamedAttribute, NamedAttribute.HasValue, ref word0))
        {
            writer.WriteBool(NamedAttribute!.Value);
        }

        if (Want(requested, Nfs4AttributeId.FileSystemId, FileSystemId.HasValue, ref word0))
        {
            FileSystemId!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.UniqueHandles, UniqueHandles.HasValue, ref word0))
        {
            writer.WriteBool(UniqueHandles!.Value);
        }

        if (Want(requested, Nfs4AttributeId.LeaseTime, LeaseTime.HasValue, ref word0))
        {
            writer.WriteUInt32(LeaseTime!.Value);
        }

        if (Want(requested, Nfs4AttributeId.ReadAttributeError, ReadAttributeError.HasValue, ref word0))
        {
            writer.WriteInt32((int)ReadAttributeError!.Value);
        }

        if (Want(requested, Nfs4AttributeId.Acl, AccessControlList is not null, ref word0))
        {
            WriteAcl(ref writer, AccessControlList!);
        }

        if (Want(requested, Nfs4AttributeId.AclSupport, AclSupport.HasValue, ref word0))
        {
            writer.WriteUInt32(AclSupport!.Value);
        }

        if (Want(requested, Nfs4AttributeId.FileHandle, FileHandle is not null, ref word0))
        {
            writer.WriteOpaqueVariable(FileHandle);
        }

        if (Want(requested, Nfs4AttributeId.FileId, FileId.HasValue, ref word0))
        {
            writer.WriteUInt64(FileId!.Value);
        }

        if (Want(requested, Nfs4AttributeId.MaxFileSize, MaxFileSize.HasValue, ref word0))
        {
            writer.WriteUInt64(MaxFileSize!.Value);
        }

        if (Want(requested, Nfs4AttributeId.MaxLink, MaxLink.HasValue, ref word0))
        {
            writer.WriteUInt32(MaxLink!.Value);
        }

        if (Want(requested, Nfs4AttributeId.MaxName, MaxName.HasValue, ref word0))
        {
            writer.WriteUInt32(MaxName!.Value);
        }

        if (Want(requested, Nfs4AttributeId.MaxRead, MaxRead.HasValue, ref word0))
        {
            writer.WriteUInt64(MaxRead!.Value);
        }

        if (Want(requested, Nfs4AttributeId.MaxWrite, MaxWrite.HasValue, ref word0))
        {
            writer.WriteUInt64(MaxWrite!.Value);
        }

        if (Want(requested, Nfs4AttributeId.Mode, Mode.HasValue, ref word1))
        {
            writer.WriteUInt32(Mode!.Value);
        }

        if (Want(requested, Nfs4AttributeId.NumLinks, NumLinks.HasValue, ref word1))
        {
            writer.WriteUInt32(NumLinks!.Value);
        }

        if (Want(requested, Nfs4AttributeId.Owner, Owner is not null, ref word1))
        {
            writer.WriteString(Owner!);
        }

        if (Want(requested, Nfs4AttributeId.OwnerGroup, OwnerGroup is not null, ref word1))
        {
            writer.WriteString(OwnerGroup!);
        }

        if (Want(requested, Nfs4AttributeId.RawDevice, RawDevice.HasValue, ref word1))
        {
            RawDevice!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.SpaceUsed, SpaceUsed.HasValue, ref word1))
        {
            writer.WriteUInt64(SpaceUsed!.Value);
        }

        if (Want(requested, Nfs4AttributeId.TimeAccess, TimeAccess.HasValue, ref word1))
        {
            TimeAccess!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.TimeMetadata, TimeMetadata.HasValue, ref word1))
        {
            TimeMetadata!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.TimeModify, TimeModify.HasValue, ref word1))
        {
            TimeModify!.Value.WriteTo(ref writer);
        }

        if (Want(requested, Nfs4AttributeId.XattrSupport, XattrSupport.HasValue, ref word2))
        {
            writer.WriteBool(XattrSupport!.Value);
        }

        uint[] mask = word2 != 0 ? [word0, word1, word2] : word1 != 0 ? [word0, word1] : word0 != 0 ? [word0] : [];
        return new Nfs4FAttr { Mask = new Nfs4Bitmap(mask), Values = values.WrittenSpan.ToArray() };
    }

    /// <summary>Decodes an <c>fattr4</c> into an attribute set.</summary>
    /// <param name="attributes">The encoded attributes.</param>
    /// <returns>The decoded attribute set.</returns>
    public static Nfs4FileAttributes Decode(Nfs4FAttr attributes)
    {
        var reader = new XdrReader(attributes.Values ?? []);
        Nfs4Bitmap mask = attributes.Mask;
        var result = new Nfs4FileAttributes();

        if (mask.IsSet(Nfs4AttributeId.SupportedAttributes))
        {
            result = result with { SupportedAttributes = Nfs4Bitmap.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.Type))
        {
            result = result with { Type = (Nfs4FileType)reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.FileHandleExpireType))
        {
            result = result with { FileHandleExpireType = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.Change))
        {
            result = result with { Change = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.Size))
        {
            result = result with { Size = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.LinkSupport))
        {
            result = result with { LinkSupport = reader.ReadBool() };
        }

        if (mask.IsSet(Nfs4AttributeId.SymlinkSupport))
        {
            result = result with { SymlinkSupport = reader.ReadBool() };
        }

        if (mask.IsSet(Nfs4AttributeId.NamedAttribute))
        {
            result = result with { NamedAttribute = reader.ReadBool() };
        }

        if (mask.IsSet(Nfs4AttributeId.FileSystemId))
        {
            result = result with { FileSystemId = Nfs4Fsid.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.UniqueHandles))
        {
            result = result with { UniqueHandles = reader.ReadBool() };
        }

        if (mask.IsSet(Nfs4AttributeId.LeaseTime))
        {
            result = result with { LeaseTime = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.ReadAttributeError))
        {
            result = result with { ReadAttributeError = (Nfs4Status)reader.ReadInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.Acl))
        {
            result = result with { AccessControlList = ReadAcl(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.AclSupport))
        {
            result = result with { AclSupport = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.FileHandle))
        {
            result = result with { FileHandle = reader.ReadOpaqueVariable(Nfs4.MaxHandleSize).ToArray() };
        }

        if (mask.IsSet(Nfs4AttributeId.FileId))
        {
            result = result with { FileId = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.MaxFileSize))
        {
            result = result with { MaxFileSize = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.MaxLink))
        {
            result = result with { MaxLink = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.MaxName))
        {
            result = result with { MaxName = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.MaxRead))
        {
            result = result with { MaxRead = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.MaxWrite))
        {
            result = result with { MaxWrite = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.Mode))
        {
            result = result with { Mode = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.NumLinks))
        {
            result = result with { NumLinks = reader.ReadUInt32() };
        }

        if (mask.IsSet(Nfs4AttributeId.Owner))
        {
            result = result with { Owner = reader.ReadString(Nfs4.MaxNameLength) };
        }

        if (mask.IsSet(Nfs4AttributeId.OwnerGroup))
        {
            result = result with { OwnerGroup = reader.ReadString(Nfs4.MaxNameLength) };
        }

        if (mask.IsSet(Nfs4AttributeId.RawDevice))
        {
            result = result with { RawDevice = Nfs4SpecData.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.SpaceUsed))
        {
            result = result with { SpaceUsed = reader.ReadUInt64() };
        }

        if (mask.IsSet(Nfs4AttributeId.TimeAccess))
        {
            result = result with { TimeAccess = Nfs4Time.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.TimeMetadata))
        {
            result = result with { TimeMetadata = Nfs4Time.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.TimeModify))
        {
            result = result with { TimeModify = Nfs4Time.ReadFrom(ref reader) };
        }

        if (mask.IsSet(Nfs4AttributeId.XattrSupport))
        {
            result = result with { XattrSupport = reader.ReadBool() };
        }

        return result;
    }

    private static void WriteAcl(ref XdrWriter writer, IReadOnlyList<NfsAccessControlEntry> entries)
    {
        writer.WriteUInt32((uint)entries.Count);
        foreach (NfsAccessControlEntry entry in entries)
        {
            writer.WriteUInt32((uint)entry.Type);
            writer.WriteUInt32((uint)entry.Flags);
            writer.WriteUInt32((uint)entry.AccessMask);
            writer.WriteString(entry.Who);
        }
    }

    private static NfsAccessControlEntry[] ReadAcl(ref XdrReader reader)
    {
        uint count = reader.ReadUInt32();
        if (count > 4096)
        {
            throw new XdrException("ACL entry count is implausibly large.");
        }

        var entries = new NfsAccessControlEntry[count];
        for (uint i = 0; i < count; i++)
        {
            entries[i] = new NfsAccessControlEntry(
                (NfsAceType)reader.ReadUInt32(),
                (NfsAceDescriptor)reader.ReadUInt32(),
                (NfsAceAccessMask)reader.ReadUInt32(),
                reader.ReadString(Nfs4.MaxNameLength));
        }

        return entries;
    }

    private static bool Want(Nfs4Bitmap requested, Nfs4AttributeId attribute, bool available, ref uint word)
    {
        if (!available || !requested.IsSet(attribute))
        {
            return false;
        }

        word |= 1u << ((int)attribute % 32);
        return true;
    }
}

/// <summary>The wire form of <c>fattr4</c> (RFC 7530): a bitmap of present attributes plus their values.</summary>
public record struct Nfs4FAttr : IXdrSerializable<Nfs4FAttr>
{
    /// <summary>The mask of attributes present in <see cref="Values"/>.</summary>
    public Nfs4Bitmap Mask { get; set; }

    /// <summary>The XDR-encoded values of the present attributes, in ascending attribute order.</summary>
    public byte[] Values { get; set; }

    /// <inheritdoc/>
    public readonly void WriteTo(ref XdrWriter writer)
    {
        Mask.WriteTo(ref writer);
        writer.WriteOpaqueVariable(Values ?? []);
    }

    /// <inheritdoc/>
    public static Nfs4FAttr ReadFrom(ref XdrReader reader) => new()
    {
        Mask = Nfs4Bitmap.ReadFrom(ref reader),
        Values = reader.ReadOpaqueVariable(int.MaxValue).ToArray(),
    };
}
