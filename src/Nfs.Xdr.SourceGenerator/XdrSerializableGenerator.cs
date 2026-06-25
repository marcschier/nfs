using System.Collections.Immutable;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Nfs.Xdr.SourceGenerator;

/// <summary>
/// Emits <c>IXdrSerializable&lt;TSelf&gt;</c> codecs for partial types annotated with
/// <c>[XdrType]</c>. Members carrying <c>[XdrField(order)]</c> are encoded in ascending order.
/// Supported members are XDR primitives, enums, strings, opaque (<c>byte[]</c>), nested
/// <c>[XdrType]</c> values, optionals (<c>T?</c>), and arrays (<c>[XdrArray] T[]</c>).
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class XdrSerializableGenerator : IIncrementalGenerator
{
    private const string XdrTypeAttribute = "Nfs.Xdr.XdrTypeAttribute";
    private const string XdrFieldAttribute = "Nfs.Xdr.XdrFieldAttribute";
    private const string XdrStringAttribute = "Nfs.Xdr.XdrStringAttribute";
    private const string XdrOpaqueAttribute = "Nfs.Xdr.XdrOpaqueAttribute";
    private const string XdrFixedOpaqueAttribute = "Nfs.Xdr.XdrFixedOpaqueAttribute";
    private const string XdrArrayAttribute = "Nfs.Xdr.XdrArrayAttribute";

    private static readonly SymbolDisplayFormat FullyQualified = SymbolDisplayFormat.FullyQualifiedFormat;

    private static readonly DiagnosticDescriptor NotPartial = new(
        "NFSXDR001",
        "XDR type must be partial",
        "Type '{0}' is annotated with [XdrType] but is not declared 'partial'; no codec was generated",
        "Nfs.Xdr",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnsupportedField = new(
        "NFSXDR002",
        "Unsupported XDR field",
        "Member '{0}' of type '{1}' cannot be encoded as XDR: {2}",
        "Nfs.Xdr",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.ForAttributeWithMetadataName(
            XdrTypeAttribute,
            predicate: static (node, _) => node is TypeDeclarationSyntax,
            transform: static (ctx, _) => CreateModel(ctx));

        context.RegisterSourceOutput(types, static (spc, model) => Emit(spc, model));
    }

    private static TypeModel CreateModel(GeneratorAttributeSyntaxContext context)
    {
        var symbol = (INamedTypeSymbol)context.TargetSymbol;
        var node = (TypeDeclarationSyntax)context.TargetNode;
        bool isPartial = node.Modifiers.Any(static m => m.IsKind(SyntaxKind.PartialKeyword));

        var fields = ImmutableArray.CreateBuilder<FieldModel>();
        var diagnostics = ImmutableArray.CreateBuilder<DiagnosticModel>();

        foreach (ISymbol member in symbol.GetMembers())
        {
            if (member.IsStatic)
            {
                continue;
            }

            AttributeData? fieldAttr = FindAttribute(member, XdrFieldAttribute);
            if (fieldAttr is null)
            {
                continue;
            }

            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol f => f.Type,
                IPropertySymbol p => p.Type,
                _ => null,
            };

            if (memberType is null)
            {
                continue;
            }

            int order = GetInt(fieldAttr, 0);
            if (TryClassify(member, memberType, order, out FieldModel? field, out DiagnosticModel? diagnostic))
            {
                fields.Add(field!);
            }
            else if (diagnostic is not null)
            {
                diagnostics.Add(diagnostic);
            }
        }

        fields.Sort(static (a, b) => a.Order.CompareTo(b.Order));

        return new TypeModel(
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            symbol.Name,
            GetTypeKeyword(symbol),
            isPartial,
            fields.ToImmutable(),
            diagnostics.ToImmutable());
    }

    private static bool TryClassify(
        ISymbol member,
        ITypeSymbol type,
        int order,
        out FieldModel? field,
        out DiagnosticModel? diagnostic)
    {
        field = null;
        diagnostic = null;

        if (type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            ITypeSymbol inner = named.TypeArguments[0];
            XdrCategory category = ClassifyLeaf(member, inner, out int bound, out string reason);
            if (!IsScalarElement(category))
            {
                diagnostic = Unsupported(member, type, category == XdrCategory.Unsupported
                    ? reason
                    : "optionals of string or opaque are not supported");
                return false;
            }

            field = new FieldModel(member.Name, order, XdrWrapper.Optional, category, inner.ToDisplayString(FullyQualified), bound);
            return true;
        }

        if (type is IArrayTypeSymbol array && array.ElementType.SpecialType != SpecialType.System_Byte)
        {
            if (!TryGetBound(member, XdrArrayAttribute, out int maxCount))
            {
                diagnostic = Unsupported(member, type, "array members require [XdrArray(maxCount)]");
                return false;
            }

            XdrCategory category = ClassifyLeaf(member, array.ElementType, out _, out string reason);
            if (!IsScalarElement(category))
            {
                diagnostic = Unsupported(member, type, category == XdrCategory.Unsupported
                    ? reason
                    : "arrays of string or opaque are not supported");
                return false;
            }

            field = new FieldModel(member.Name, order, XdrWrapper.Array, category, array.ElementType.ToDisplayString(FullyQualified), maxCount);
            return true;
        }

        XdrCategory leaf = ClassifyLeaf(member, type, out int leafBound, out string leafReason);
        if (leaf == XdrCategory.Unsupported)
        {
            diagnostic = Unsupported(member, type, leafReason);
            return false;
        }

        field = new FieldModel(member.Name, order, XdrWrapper.Leaf, leaf, type.ToDisplayString(FullyQualified), leafBound);
        return true;
    }

    private static XdrCategory ClassifyLeaf(ISymbol member, ITypeSymbol type, out int bound, out string reason)
    {
        bound = 0;
        reason = string.Empty;

        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol named)
        {
            return named.EnumUnderlyingType?.SpecialType switch
            {
                SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_Int32 => XdrCategory.EnumInt32,
                SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 => XdrCategory.EnumUInt32,
                SpecialType.System_Int64 => XdrCategory.EnumInt64,
                SpecialType.System_UInt64 => XdrCategory.EnumUInt64,
                _ => XdrCategory.EnumInt32,
            };
        }

        switch (type.SpecialType)
        {
            case SpecialType.System_Int32:
                return XdrCategory.Int32;
            case SpecialType.System_UInt32:
                return XdrCategory.UInt32;
            case SpecialType.System_Int64:
                return XdrCategory.Int64;
            case SpecialType.System_UInt64:
                return XdrCategory.UInt64;
            case SpecialType.System_Boolean:
                return XdrCategory.Bool;
            case SpecialType.System_Single:
                return XdrCategory.Single;
            case SpecialType.System_Double:
                return XdrCategory.Double;
            case SpecialType.System_String:
                if (TryGetBound(member, XdrStringAttribute, out bound))
                {
                    return XdrCategory.String;
                }

                reason = "string members require [XdrString(maxLength)]";
                return XdrCategory.Unsupported;
        }

        if (type is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte })
        {
            if (TryGetBound(member, XdrOpaqueAttribute, out bound))
            {
                return XdrCategory.OpaqueVariable;
            }

            if (TryGetBound(member, XdrFixedOpaqueAttribute, out bound))
            {
                return XdrCategory.OpaqueFixed;
            }

            reason = "byte[] members require [XdrOpaque(maxLength)] or [XdrFixedOpaque(length)]";
            return XdrCategory.Unsupported;
        }

        if (HasXdrType(type) || ImplementsXdrSerializable(type))
        {
            return XdrCategory.Serializable;
        }

        reason = "the type is not an XDR primitive, enum, byte[], or an [XdrType] type";
        return XdrCategory.Unsupported;
    }

    private static void Emit(SourceProductionContext context, TypeModel model)
    {
        foreach (DiagnosticModel diagnostic in model.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic());
        }

        if (!model.IsPartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(NotPartial, Location.None, model.Name));
            return;
        }

        if (model.Diagnostics.Length != 0)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        bool hasNamespace = model.Namespace is not null;
        string indent = hasNamespace ? "    " : string.Empty;
        if (hasNamespace)
        {
            sb.Append("namespace ").Append(model.Namespace).AppendLine();
            sb.AppendLine("{");
        }

        sb.Append(indent).Append("partial ").Append(model.TypeKeyword).Append(' ').Append(model.Name)
            .Append(" : global::Nfs.Xdr.IXdrSerializable<").Append(model.Name).Append('>').AppendLine();
        sb.Append(indent).AppendLine("{");

        string body = indent + "        ";

        sb.Append(indent).AppendLine("    /// <inheritdoc/>");
        sb.Append(indent).AppendLine("    public void WriteTo(ref global::Nfs.Xdr.XdrWriter writer)");
        sb.Append(indent).AppendLine("    {");
        foreach (FieldModel field in model.Fields)
        {
            AppendWrite(sb, body, field);
        }

        sb.Append(indent).AppendLine("    }");
        sb.AppendLine();

        sb.Append(indent).AppendLine("    /// <inheritdoc/>");
        sb.Append(indent).Append("    public static ").Append(model.Name)
            .AppendLine(" ReadFrom(ref global::Nfs.Xdr.XdrReader reader)");
        sb.Append(indent).AppendLine("    {");
        sb.Append(indent).Append("        var value = new ").Append(model.Name).AppendLine("();");
        foreach (FieldModel field in model.Fields)
        {
            AppendRead(sb, body, field);
        }

        sb.Append(indent).AppendLine("        return value;");
        sb.Append(indent).AppendLine("    }");
        sb.Append(indent).AppendLine("}");

        if (hasNamespace)
        {
            sb.AppendLine("}");
        }

        string hint = (model.Namespace is null ? string.Empty : model.Namespace + ".") + model.Name + ".Xdr.g.cs";
        context.AddSource(hint, sb.ToString());
    }

    private static void AppendWrite(StringBuilder sb, string indent, FieldModel field)
    {
        switch (field.Wrapper)
        {
            case XdrWrapper.Leaf:
                sb.Append(indent).AppendLine(WriteLeaf(field.Category, field.Name));
                break;

            case XdrWrapper.Optional:
                sb.Append(indent).Append("if (").Append(field.Name).AppendLine(".HasValue)");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).AppendLine("    writer.WriteBool(true);");
                sb.Append(indent).Append("    ").AppendLine(WriteLeaf(field.Category, field.Name + ".Value"));
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).AppendLine("    writer.WriteBool(false);");
                sb.Append(indent).AppendLine("}");
                break;

            case XdrWrapper.Array:
                sb.Append(indent).Append("if (").Append(field.Name).AppendLine(" is null)");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).AppendLine("    writer.WriteUInt32(0);");
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).Append("    writer.WriteUInt32((uint)").Append(field.Name).AppendLine(".Length);");
                sb.Append(indent).Append("    foreach (var __item in ").Append(field.Name).AppendLine(")");
                sb.Append(indent).AppendLine("    {");
                sb.Append(indent).Append("        ").AppendLine(WriteLeaf(field.Category, "__item"));
                sb.Append(indent).AppendLine("    }");
                sb.Append(indent).AppendLine("}");
                break;

            default:
                break;
        }
    }

    private static void AppendRead(StringBuilder sb, string indent, FieldModel field)
    {
        switch (field.Wrapper)
        {
            case XdrWrapper.Leaf:
                sb.Append(indent).Append("value.").Append(field.Name).Append(" = ")
                    .Append(ReadLeaf(field.Category, field.TypeFullName, field.Bound)).AppendLine(";");
                break;

            case XdrWrapper.Optional:
                sb.Append(indent).AppendLine("if (reader.ReadBool())");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).Append("    value.").Append(field.Name).Append(" = ")
                    .Append(ReadLeaf(field.Category, field.TypeFullName, field.Bound)).AppendLine(";");
                sb.Append(indent).AppendLine("}");
                sb.Append(indent).AppendLine("else");
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).Append("    value.").Append(field.Name).AppendLine(" = null;");
                sb.Append(indent).AppendLine("}");
                break;

            case XdrWrapper.Array:
                sb.Append(indent).AppendLine("{");
                sb.Append(indent).Append("    int __count = reader.ReadLength(").Append(field.Bound).AppendLine(");");
                sb.Append(indent).Append("    var __array = new ").Append(field.TypeFullName).AppendLine("[__count];");
                sb.Append(indent).AppendLine("    for (int __i = 0; __i < __count; __i++)");
                sb.Append(indent).AppendLine("    {");
                sb.Append(indent).Append("        __array[__i] = ")
                    .Append(ReadLeaf(field.Category, field.TypeFullName, field.Bound)).AppendLine(";");
                sb.Append(indent).AppendLine("    }");
                sb.Append(indent).Append("    value.").Append(field.Name).AppendLine(" = __array;");
                sb.Append(indent).AppendLine("}");
                break;

            default:
                break;
        }
    }

    private static string WriteLeaf(XdrCategory category, string expr) => category switch
    {
        XdrCategory.Int32 => $"writer.WriteInt32({expr});",
        XdrCategory.UInt32 => $"writer.WriteUInt32({expr});",
        XdrCategory.Int64 => $"writer.WriteInt64({expr});",
        XdrCategory.UInt64 => $"writer.WriteUInt64({expr});",
        XdrCategory.Bool => $"writer.WriteBool({expr});",
        XdrCategory.Single => $"writer.WriteSingle({expr});",
        XdrCategory.Double => $"writer.WriteDouble({expr});",
        XdrCategory.EnumInt32 => $"writer.WriteInt32((int){expr});",
        XdrCategory.EnumUInt32 => $"writer.WriteUInt32((uint){expr});",
        XdrCategory.EnumInt64 => $"writer.WriteInt64((long){expr});",
        XdrCategory.EnumUInt64 => $"writer.WriteUInt64((ulong){expr});",
        XdrCategory.String => $"writer.WriteString({expr});",
        XdrCategory.OpaqueVariable => $"writer.WriteOpaqueVariable({expr});",
        XdrCategory.OpaqueFixed => $"writer.WriteOpaqueFixed({expr});",
        XdrCategory.Serializable => $"{expr}.WriteTo(ref writer);",
        _ => string.Empty,
    };

    private static string ReadLeaf(XdrCategory category, string typeFullName, int bound) => category switch
    {
        XdrCategory.Int32 => "reader.ReadInt32()",
        XdrCategory.UInt32 => "reader.ReadUInt32()",
        XdrCategory.Int64 => "reader.ReadInt64()",
        XdrCategory.UInt64 => "reader.ReadUInt64()",
        XdrCategory.Bool => "reader.ReadBool()",
        XdrCategory.Single => "reader.ReadSingle()",
        XdrCategory.Double => "reader.ReadDouble()",
        XdrCategory.EnumInt32 => $"({typeFullName})reader.ReadInt32()",
        XdrCategory.EnumUInt32 => $"({typeFullName})reader.ReadUInt32()",
        XdrCategory.EnumInt64 => $"({typeFullName})reader.ReadInt64()",
        XdrCategory.EnumUInt64 => $"({typeFullName})reader.ReadUInt64()",
        XdrCategory.String => $"reader.ReadString({bound})",
        XdrCategory.OpaqueVariable => $"reader.ReadOpaqueVariable({bound}).ToArray()",
        XdrCategory.OpaqueFixed => $"reader.ReadOpaqueFixed({bound}).ToArray()",
        XdrCategory.Serializable => $"{typeFullName}.ReadFrom(ref reader)",
        _ => string.Empty,
    };

    private static bool IsScalarElement(XdrCategory category) => category is not (
        XdrCategory.Unsupported or XdrCategory.String or XdrCategory.OpaqueVariable or XdrCategory.OpaqueFixed);

    private static DiagnosticModel Unsupported(ISymbol member, ITypeSymbol type, string reason) =>
        new(UnsupportedField, member.Locations.FirstOrDefault(), member.Name, type.ToDisplayString(), reason);

    private static string GetTypeKeyword(INamedTypeSymbol symbol) => (symbol.IsRecord, symbol.IsValueType) switch
    {
        (true, true) => "record struct",
        (true, false) => "record",
        (false, true) => "struct",
        (false, false) => "class",
    };

    private static bool ImplementsXdrSerializable(ITypeSymbol type) =>
        type.AllInterfaces.Any(static i =>
            i.MetadataName == "IXdrSerializable`1" &&
            i.ContainingNamespace.ToDisplayString() == "Nfs.Xdr");

    private static bool HasXdrType(ITypeSymbol type) =>
        type.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == XdrTypeAttribute);

    private static AttributeData? FindAttribute(ISymbol symbol, string fullName) =>
        symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == fullName);

    private static bool TryGetBound(ISymbol member, string attributeName, out int bound)
    {
        AttributeData? attr = FindAttribute(member, attributeName);
        if (attr is null)
        {
            bound = 0;
            return false;
        }

        bound = GetInt(attr, 0);
        return true;
    }

    private static int GetInt(AttributeData attribute, int index) =>
        attribute.ConstructorArguments.Length > index && attribute.ConstructorArguments[index].Value is int value
            ? value
            : 0;
}
