using Nfs.Abstractions;
using Nfs.Protocol.V4;

namespace Nfs.Server;

/// <summary>Maps between the abstraction types and the NFS version 4 wire types.</summary>
internal static class Nfs4Mapping
{
    /// <summary>The full set of attributes this server can report.</summary>
    public static readonly Nfs4Bitmap SupportedAttributes = Nfs4Bitmap.Of(
        Nfs4AttributeId.SupportedAttributes,
        Nfs4AttributeId.Type,
        Nfs4AttributeId.FileHandleExpireType,
        Nfs4AttributeId.Change,
        Nfs4AttributeId.Size,
        Nfs4AttributeId.LinkSupport,
        Nfs4AttributeId.SymlinkSupport,
        Nfs4AttributeId.NamedAttribute,
        Nfs4AttributeId.FileSystemId,
        Nfs4AttributeId.UniqueHandles,
        Nfs4AttributeId.LeaseTime,
        Nfs4AttributeId.ReadAttributeError,
        Nfs4AttributeId.Acl,
        Nfs4AttributeId.AclSupport,
        Nfs4AttributeId.FileHandle,
        Nfs4AttributeId.FileId,
        Nfs4AttributeId.MaxFileSize,
        Nfs4AttributeId.MaxLink,
        Nfs4AttributeId.MaxName,
        Nfs4AttributeId.MaxRead,
        Nfs4AttributeId.MaxWrite,
        Nfs4AttributeId.Mode,
        Nfs4AttributeId.NumLinks,
        Nfs4AttributeId.Owner,
        Nfs4AttributeId.OwnerGroup,
        Nfs4AttributeId.RawDevice,
        Nfs4AttributeId.SpaceUsed,
        Nfs4AttributeId.TimeAccess,
        Nfs4AttributeId.TimeMetadata,
        Nfs4AttributeId.TimeModify,
        Nfs4AttributeId.XattrSupport);

    /// <summary>The server's lease time, in seconds.</summary>
    public const uint LeaseTimeSeconds = 90;

    /// <summary>The ACL ACE types this server can store and evaluate.</summary>
    public const uint AclSupport = 0x00000001 | 0x00000002;

    public static NfsFileHandle ToHandle(Nfs4Handle wire)
    {
        if (wire.Data is not { Length: > 0 })
        {
            throw new NfsException(NfsStatus.BadHandle);
        }

        return new NfsFileHandle(wire.Data);
    }

    public static Nfs4Handle ToWire(NfsFileHandle handle) => new() { Data = handle.ToArray() };

    public static Nfs4Time ToWire(NfsTimestamp timestamp) =>
        new() { Seconds = timestamp.Seconds, Nanoseconds = timestamp.Nanoseconds };

    /// <summary>Computes a monotonic change identifier from an object's change time.</summary>
    /// <param name="attributes">The object's attributes.</param>
    /// <returns>The change identifier.</returns>
    public static ulong ChangeId(NfsFileAttributes attributes) =>
        ((ulong)attributes.ChangeTime.Seconds << 32) | attributes.ChangeTime.Nanoseconds;

    /// <summary>Builds the full version 4 attribute set for an object; the caller selects the subset to send.</summary>
    /// <param name="attributes">The object's attributes.</param>
    /// <param name="handle">The object's handle.</param>
    /// <param name="accessControlList">The object's ACL, when requested.</param>
    /// <returns>The populated attribute set.</returns>
    public static Nfs4FileAttributes BuildAttributes(
        NfsFileAttributes attributes,
        NfsFileHandle handle,
        IReadOnlyList<NfsAccessControlEntry>? accessControlList = null) => new()
        {
            SupportedAttributes = SupportedAttributes,
            Type = (Nfs4FileType)attributes.Type,
            FileHandleExpireType = 0,
            Change = ChangeId(attributes),
            Size = attributes.Size,
            LinkSupport = true,
            SymlinkSupport = true,
            NamedAttribute = false,
            FileSystemId = new Nfs4Fsid { Major = 0, Minor = 0 },
            UniqueHandles = true,
            LeaseTime = LeaseTimeSeconds,
            ReadAttributeError = Nfs4Status.Ok,
            AccessControlList = accessControlList,
            AclSupport = AclSupport,
            FileHandle = handle.ToArray(),
            FileId = attributes.FileId,
            MaxFileSize = long.MaxValue,
            MaxLink = 32000,
            MaxName = Nfs4.MaxNameLength,
            MaxRead = Nfs4.MaxIoSize,
            MaxWrite = Nfs4.MaxIoSize,
            Mode = attributes.Mode,
            NumLinks = attributes.LinkCount,
            Owner = attributes.Uid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            OwnerGroup = attributes.Gid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RawDevice = new Nfs4SpecData { Major = 0, Minor = 0 },
            SpaceUsed = attributes.Used,
            TimeAccess = ToWire(attributes.AccessTime),
            TimeMetadata = ToWire(attributes.ChangeTime),
            TimeModify = ToWire(attributes.ModifyTime),
            XattrSupport = true,
        };

    /// <summary>Converts a decoded <c>fattr4</c> into version-independent attribute changes.</summary>
    /// <param name="attributes">The encoded attributes to apply.</param>
    /// <returns>The attribute changes plus the bitmap of attributes recognized.</returns>
    public static (NfsSetAttributes Changes, Nfs4Bitmap Applied) ToSetAttributes(Nfs4FAttr attributes)
    {
        Nfs4FileAttributes decoded = Nfs4FileAttributes.Decode(attributes);
        var changes = new NfsSetAttributes
        {
            Mode = decoded.Mode,
            Size = decoded.Size,
            AccessTime = decoded.TimeAccess is { } at ? new NfsTimestamp((uint)at.Seconds, at.Nanoseconds) : null,
            ModifyTime = decoded.TimeModify is { } mt ? new NfsTimestamp((uint)mt.Seconds, mt.Nanoseconds) : null,
            AccessControlList = decoded.AccessControlList,
        };

        var applied = new List<Nfs4AttributeId>();
        if (decoded.Mode.HasValue)
        {
            applied.Add(Nfs4AttributeId.Mode);
        }

        if (decoded.Size.HasValue)
        {
            applied.Add(Nfs4AttributeId.Size);
        }

        if (decoded.TimeAccess.HasValue)
        {
            applied.Add(Nfs4AttributeId.TimeAccess);
        }

        if (decoded.TimeModify.HasValue)
        {
            applied.Add(Nfs4AttributeId.TimeModify);
        }

        if (decoded.AccessControlList is not null)
        {
            applied.Add(Nfs4AttributeId.Acl);
        }

        return (changes, Nfs4Bitmap.Of([.. applied]));
    }
}
