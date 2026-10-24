using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // A containing type's declaration parameters describe its body. A call
    // through Box<P> sees P, while a call through Box<int> sees concrete int.
    private TypeExpression BindCallableOwnerType(MethodDeclaration method, AstNode target, TypeExpression type)
    {
        if (!TryGetMethodOwner(method, out var declaringOwner, out _)) return type;
        if (target is UnaryExpression { Operator: TokenKind.Ampersand } address) target = address.Operand;
        TypeExpression? receiver = target switch
        {
            MemberAccessExpression member when member.Object is not IdentifierExpression { Name: "this" } => _types.GetValueOrDefault(member.Object),
            NamespaceAccessExpression access => StaticOwnerType(access.Left) is { } owner ? ResolveAlias(owner) : null,
            _ => null
        };
        if (receiver == null) return type;
        receiver = ResolveAlias(receiver);
        receiver = receiver switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression p => p.Inner,
            ReferenceTypeExpression r => r.Inner, _ => receiver };
        if (PrimitiveGenericMembers(receiver, declaringOwner) is not { } generic) return type;
        var definition = generic.Definition;
        var arguments = generic.Bindings;
        return Bind(type);

        TypeExpression Bind(TypeExpression source)
        {
            if (source.GenericParameterView is { } parameter && ReferenceEquals(parameter.DeclaringDefinition, definition) &&
                arguments.TryGetValue(parameter.ParameterName, out var argument)) return argument.WithQualifiersFrom(source);
            TypeExpression result = source switch
            {
                PointerTypeExpression p => new PointerTypeExpression(Bind(p.Inner), p.IsNullable, p.Line, p.IsReadOnly),
                ManagedTypeExpression p => new ManagedTypeExpression(Bind(p.Inner), p.IsNullable, p.Line, p.IsReadOnly,
                    p.Collector == null ? null : Bind(p.Collector)) { UsesDefaultCollector = p.UsesDefaultCollector },
                ReferenceTypeExpression r => new ReferenceTypeExpression(Bind(r.Inner), r.Line),
                ArrayTypeExpression a => new ArrayTypeExpression(Bind(a.ElementType), a.Size, a.Line, a.SizeExpression),
                FunctionPointerTypeExpression f => new FunctionPointerTypeExpression(Bind(f.ReturnType), f.ParameterTypes.Select(Bind).ToList(),
                    f.IsManaged, f.IsNullable, f.Line, f.IsSignature),
                _ => source
            };
            if (source.GenericUse is { } origin) result = result.WithGenericUse(CopyNamed(origin, origin.TypeArguments.Select(Bind).ToList()));
            return result.WithQualifiersFrom(source);
        }
    }
}
