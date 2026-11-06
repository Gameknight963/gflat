using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class TypeChecker
{
    private bool TryReflectPropertyMember(ConstValue.Reflection target, string member, int line, out ConstValue? result)
    {
        result = null;
        if (target.Kind == "Type" && target.Type is { } type && ResolveAlias(type) is NamedTypeExpression named &&
            member is "Properties" or "StaticProperties")
        {
            var declaration = GetStructByIdentity(named.Name)?.Declaration ?? (AstNode?)GetClassByIdentity(named.Name)?.Declaration
                ?? ResolveInterface(named)?.Declaration;
            IEnumerable<AstNode> members = declaration switch {
                StructDeclaration s => s.Members, ClassDeclaration c => c.Members, InterfaceDeclaration i => i.Members, _ => []
            };
            if (declaration == null) { result = new ConstValue.Array([]); return true; }
            using var context = EnterSignatureContext(declaration, named.Name, GetClassByIdentity(named.Name) != null);
            result = new ConstValue.Array(members.OfType<PropertyDeclaration>()
                .Where(p => (p.Getter ?? p.Setter)!.IsStatic == (member == "StaticProperties"))
                .Select(p => (ConstValue)new ConstValue.Reflection("property:" + GetTypeMangledName(type) + ":" + p.Name,
                    "Property", p.Name, ResolveAlias(p.Type), p, type)).ToList());
            return true;
        }
        if (target.Declaration is not PropertyDeclaration property) return false;
        if (member == "HasGetter") result = new ConstValue.Boolean(property.Getter != null);
        else if (member == "HasSetter") result = new ConstValue.Boolean(property.Setter != null);
        else if (member == "IsRef") result = new ConstValue.Boolean(property.IsRef);
        else if (member == "IsStatic") result = new ConstValue.Boolean((property.Getter ?? property.Setter)!.IsStatic);
        else if (member is "Accessibility" or "IsPublic" or "IsPrivate")
        {
            var accessors = new[] { property.Getter, property.Setter }.OfType<MethodDeclaration>().ToArray();
            var access = accessors.Any(a => a.Accessibility == TokenKind.Public) ? TokenKind.Public :
                accessors.Any(a => a.Accessibility == TokenKind.Protected) ? TokenKind.Protected :
                accessors.Any(a => a.Accessibility == TokenKind.Internal) ? TokenKind.Internal : TokenKind.Private;
            result = member switch {
                "IsPublic" => new ConstValue.Boolean(access == TokenKind.Public),
                "IsPrivate" => new ConstValue.Boolean(access == TokenKind.Private),
                _ => new ConstValue.String(access.ToString().ToLowerInvariant())
            };
        }
        return result != null;
    }
}
