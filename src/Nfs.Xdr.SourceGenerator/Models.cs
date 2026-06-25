using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace Nfs.Xdr.SourceGenerator;

/// <summary>How a member is wrapped relative to its underlying (leaf) XDR encoding.</summary>
internal enum XdrWrapper
{
    /// <summary>The member is encoded directly as its leaf category.</summary>
    Leaf,

    /// <summary>The member is a <c>T?</c> encoded as a bool flag followed by the value when present.</summary>
    Optional,

    /// <summary>The member is a <c>T[]</c> encoded as a count followed by that many elements.</summary>
    Array,
}

/// <summary>The leaf XDR encoding of a value.</summary>
internal enum XdrCategory
{
    Unsupported,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Bool,
    Single,
    Double,
    EnumInt32,
    EnumUInt32,
    EnumInt64,
    EnumUInt64,
    String,
    OpaqueVariable,
    OpaqueFixed,
    Serializable,
}

/// <summary>A member that participates in a type's XDR encoding.</summary>
/// <param name="Name">The member name.</param>
/// <param name="Order">The encoding order.</param>
/// <param name="Wrapper">Whether the member is a leaf, an optional, or an array.</param>
/// <param name="Category">The leaf encoding of the member (or its element type, when wrapped).</param>
/// <param name="TypeFullName">The fully-qualified leaf/element type, for casts and construction.</param>
/// <param name="Bound">A string/opaque max length, or, for arrays, the maximum element count.</param>
internal sealed record FieldModel(
    string Name,
    int Order,
    XdrWrapper Wrapper,
    XdrCategory Category,
    string TypeFullName,
    int Bound);

internal sealed record TypeModel(
    string? Namespace,
    string Name,
    string TypeKeyword,
    bool IsPartial,
    ImmutableArray<FieldModel> Fields,
    ImmutableArray<DiagnosticModel> Diagnostics);

internal sealed class DiagnosticModel
{
    private readonly DiagnosticDescriptor _descriptor;
    private readonly Location? _location;
    private readonly object?[] _args;

    public DiagnosticModel(DiagnosticDescriptor descriptor, Location? location, params object?[] args)
    {
        _descriptor = descriptor;
        _location = location;
        _args = args;
    }

    public Diagnostic ToDiagnostic() => Diagnostic.Create(_descriptor, _location ?? Location.None, _args);
}
