namespace Nfs.Xdr;

/// <summary>
/// Marks a <see langword="partial"/> type for which the XDR source generator emits an
/// <see cref="IXdrSerializable{TSelf}"/> codec. Members that participate in the encoding are
/// annotated with <see cref="XdrFieldAttribute"/> and are encoded in ascending order.
/// </summary>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class)]
public sealed class XdrTypeAttribute : Attribute
{
}

/// <summary>
/// Declares that a field or property is part of its declaring type's XDR encoding, and fixes
/// its position in the encoded stream.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class XdrFieldAttribute : Attribute
{
    /// <summary>Initializes a new <see cref="XdrFieldAttribute"/>.</summary>
    /// <param name="order">The zero-based position of this member within the encoded stream.</param>
    public XdrFieldAttribute(int order) => Order = order;

    /// <summary>Gets the zero-based position of this member within the encoded stream.</summary>
    public int Order { get; }
}

/// <summary>
/// Specifies that a <see cref="string"/> member is encoded as a variable-length XDR string,
/// bounded by a maximum byte length.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class XdrStringAttribute : Attribute
{
    /// <summary>Initializes a new <see cref="XdrStringAttribute"/>.</summary>
    /// <param name="maxLength">The largest encoded byte length permitted when decoding.</param>
    public XdrStringAttribute(int maxLength) => MaxLength = maxLength;

    /// <summary>Gets the largest encoded byte length permitted when decoding.</summary>
    public int MaxLength { get; }
}

/// <summary>
/// Specifies that a <see cref="byte"/> array member is encoded as a variable-length XDR opaque
/// value, bounded by a maximum length.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class XdrOpaqueAttribute : Attribute
{
    /// <summary>Initializes a new <see cref="XdrOpaqueAttribute"/>.</summary>
    /// <param name="maxLength">The largest length permitted when decoding.</param>
    public XdrOpaqueAttribute(int maxLength) => MaxLength = maxLength;

    /// <summary>Gets the largest length permitted when decoding.</summary>
    public int MaxLength { get; }
}

/// <summary>
/// Specifies that a <see cref="byte"/> array member is encoded as a fixed-length XDR opaque value
/// of an exact size.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class XdrFixedOpaqueAttribute : Attribute
{
    /// <summary>Initializes a new <see cref="XdrFixedOpaqueAttribute"/>.</summary>
    /// <param name="length">The exact number of bytes encoded.</param>
    public XdrFixedOpaqueAttribute(int length) => Length = length;

    /// <summary>Gets the exact number of bytes encoded.</summary>
    public int Length { get; }
}

/// <summary>
/// Specifies that an array member (other than <see cref="byte"/>[]) is encoded as a variable-length
/// XDR array: a 32-bit element count followed by that many encoded elements. The element type must
/// be an XDR primitive, an enum, or an <see cref="XdrTypeAttribute"/> type.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public sealed class XdrArrayAttribute : Attribute
{
    /// <summary>Initializes a new <see cref="XdrArrayAttribute"/>.</summary>
    /// <param name="maxCount">The largest element count permitted when decoding.</param>
    public XdrArrayAttribute(int maxCount) => MaxCount = maxCount;

    /// <summary>Gets the largest element count permitted when decoding.</summary>
    public int MaxCount { get; }
}
