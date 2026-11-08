using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public sealed record BinaryEnumMember(string Name, string Value);
public sealed record BinaryEnum(string Namespace, string Name, string UnderlyingType, IReadOnlyList<BinaryEnumMember> Members)
{
    public string QualifiedName => Namespace.Length == 0 ? Name : Namespace + "::" + Name;
}

public sealed partial class BinaryInterface
{
    private static IReadOnlyList<BinaryEnum> ExportEnums(CompilationUnit unit, TypeChecker checker, IReadOnlySet<string> paths)
    {
        var result = new List<BinaryEnum>();
        Walk(unit.Members);
        foreach (var ns in unit.Namespaces) Walk(ns.Members);
        return result.OrderBy(enumeration => enumeration.QualifiedName, StringComparer.Ordinal).ToArray();

        void Walk(IEnumerable<AstNode> members)
        {
            foreach (var member in members)
            {
                if (member is NamespaceDeclaration ns) { Walk(ns.Members); continue; }
                if (member is not EnumDeclaration declaration || declaration.Accessibility != TokenKind.Public ||
                    declaration.Span.DeclarationSourceId is not { } owner || owner.Path.StartsWith('<') ||
                    !paths.Contains(Path.GetFullPath(owner.Path))) continue;
                var info = checker.GetEnum(declaration.Name) ?? throw new TypeCheckException("Cannot resolve exported enum", declaration.Span);
                string storage = checker.DisplayTypeName(checker.ResolveAlias(info.UnderlyingType).WithoutValueProtection());
                bool unsigned = checker.IsUnsignedInteger(info.UnderlyingType);
                string qualified = checker.DisplayTypeName(NamedTypeExpression.Resolved(info.Name, declaration.Line));
                int separator = qualified.LastIndexOf("::", StringComparison.Ordinal);
                var enumeration = new BinaryEnum(separator < 0 ? "" : qualified[..separator], separator < 0 ? qualified : qualified[(separator + 2)..], storage,
                    info.Members.Select(pair => new BinaryEnumMember(pair.Key,
                        unsigned ? unchecked((ulong)pair.Value).ToString(CultureInfo.InvariantCulture) : pair.Value.ToString(CultureInfo.InvariantCulture))).ToArray());
                ValidateEnum(enumeration, declaration.Span);
                result.Add(enumeration);
            }
        }
    }

    private static void ValidateEnum(BinaryEnum enumeration, SourceSpan span)
    {
        bool Identifier(string value) => Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$") && !CppKeywords.Contains(value);
        string? invalidName = new[] { enumeration.Name }.Concat(enumeration.Namespace.Split("::", StringSplitOptions.RemoveEmptyEntries))
            .Concat(enumeration.Members.Select(member => member.Name)).FirstOrDefault(name => !Identifier(name));
        if (invalidName != null)
            throw new TypeCheckException($"Binary enum identifier '{invalidName}' cannot be used in the generated C++ header", span);
        if (enumeration.UnderlyingType is not ("char" or "sbyte" or "byte" or "short" or "ushort" or "int" or "uint" or "long" or "ulong" or "nint" or "nuint"))
            throw new TypeCheckException("Unsupported binary enum storage type: " + enumeration.UnderlyingType, span);
        if (enumeration.Members.FirstOrDefault(member => !Regex.IsMatch(member.Value, "^-?[0-9]+$")) is { } invalidMember)
            throw new TypeCheckException("Invalid integer constant for binary enum member: " + invalidMember.Name, span);
    }

    private static string EnumDeclarationText(BinaryEnum enumeration, bool cpp)
    {
        string storage = cpp ? CppHeaderType(enumeration.UnderlyingType == "char" ? "unsigned char" :
            CppLinkage.Type(new NamedTypeExpression(enumeration.UnderlyingType, null, 0))) : enumeration.UnderlyingType;
        bool unsigned = enumeration.UnderlyingType is "char" or "byte" or "ushort" or "uint" or "ulong" or "nuint";
        string Literal(string value) => cpp && value == "-9223372036854775808" ? "(-9223372036854775807LL - 1)" :
            value + (cpp ? unsigned ? "ULL" : "LL" : unsigned ? "ul" : "l");
        string declaration = (cpp ? "enum class " : "public enum @") + enumeration.Name + " : " + storage + " { " +
            string.Join(", ", enumeration.Members.Select(member => (cpp ? "" : "@") + member.Name + " = " + Literal(member.Value))) + (cpp ? " };" : " }");
        return InNamespace(enumeration.Namespace, declaration, escaped: !cpp);
    }

    private static BinaryEnum ReadEnum(XElement element, SourceSpan span)
    {
        string Required(XElement node, string attribute) => (string?)node.Attribute(attribute) ??
            throw new TypeCheckException("Missing " + attribute + " in binary enum", span);
        var enumeration = new BinaryEnum(Required(element, "Namespace"), Required(element, "Name"), Required(element, "UnderlyingType"),
            element.Elements("Member").Select(member => new BinaryEnumMember(Required(member, "Name"), Required(member, "Value"))).ToArray());
        ValidateEnum(enumeration, span);
        return enumeration;
    }

    private static XElement EnumXml(BinaryEnum enumeration) => new("Enum", new XAttribute("Namespace", enumeration.Namespace),
        new XAttribute("Name", enumeration.Name), new XAttribute("UnderlyingType", enumeration.UnderlyingType),
        enumeration.Members.Select(member => new XElement("Member", new XAttribute("Name", member.Name), new XAttribute("Value", member.Value))));

    private static string CppHeaderType(string value)
        => value.Replace("__INTPTR_TYPE__", "std::intptr_t").Replace("__UINTPTR_TYPE__", "std::uintptr_t");
}
