using Nfs.Abstractions;

namespace Nfs.Server;

internal static class NfsMetadataHelpers
{
    public const int MaxExtendedAttributeNameBytes = 255;
    public const int MaxExtendedAttributeValueBytes = 65536;
    public const int MaxExtendedAttributesPerObject = 128;

    public static IReadOnlyList<NfsAccessControlEntry> AclFromMode(uint mode)
    {
        return
        [
            new(NfsAceType.Allow, NfsAceDescriptor.None, MaskFromBits((mode >> 6) & 7), "OWNER@"),
            new(NfsAceType.Allow, NfsAceDescriptor.IdentifierGroup, MaskFromBits((mode >> 3) & 7), "GROUP@"),
            new(NfsAceType.Allow, NfsAceDescriptor.None, MaskFromBits(mode & 7), "EVERYONE@"),
        ];
    }

    public static void ValidateExtendedAttributeName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\'))
        {
            throw new NfsException(NfsStatus.InvalidArgument);
        }

        if (System.Text.Encoding.UTF8.GetByteCount(name) > MaxExtendedAttributeNameBytes)
        {
            throw new NfsException(NfsStatus.NameTooLong);
        }
    }

    public static void ValidateExtendedAttributeValue(ReadOnlyMemory<byte> value)
    {
        if (value.Length > MaxExtendedAttributeValueBytes)
        {
            throw new NfsException(NfsStatus.ExtendedAttributeTooBig);
        }
    }

    public static NfsExtendedAttributeListing ListExtendedAttributes(
        IReadOnlyCollection<string> names,
        ulong cookie,
        uint maxCount)
    {
        string[] ordered = [.. names.OrderBy(static name => name, StringComparer.Ordinal)];
        if (cookie > (ulong)ordered.Length)
        {
            throw new NfsException(NfsStatus.BadCookie);
        }

        var result = new List<string>();
        uint encoded = 16;
        int index = (int)cookie;
        for (; index < ordered.Length; index++)
        {
            string name = ordered[index];
            uint next = checked(encoded + 4u + Align4((uint)System.Text.Encoding.UTF8.GetByteCount(name)));
            if (result.Count > 0 && next > maxCount)
            {
                break;
            }

            if (result.Count == 0 && next > maxCount)
            {
                throw new NfsException(NfsStatus.TooSmall);
            }

            encoded = next;
            result.Add(name);
        }

        return new NfsExtendedAttributeListing(result, (ulong)index, index >= ordered.Length);
    }

    private static NfsAceAccessMask MaskFromBits(uint bits)
    {
        NfsAceAccessMask mask = NfsAceAccessMask.ReadAttributes | NfsAceAccessMask.ReadAcl | NfsAceAccessMask.Synchronize;
        if ((bits & 4) != 0)
        {
            mask |= NfsAceAccessMask.ReadData;
        }

        if ((bits & 2) != 0)
        {
            mask |= NfsAceAccessMask.WriteData | NfsAceAccessMask.AppendData | NfsAceAccessMask.WriteAttributes;
        }

        if ((bits & 1) != 0)
        {
            mask |= NfsAceAccessMask.Execute;
        }

        return mask;
    }

    private static uint Align4(uint value) => (value + 3u) & ~3u;
}
