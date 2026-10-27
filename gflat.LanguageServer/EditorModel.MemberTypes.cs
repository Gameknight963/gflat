using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private Symbol ProjectSourceMemberType(Symbol source, TypeExpression? receiver, string path, int offset)
    {
        if (source.Static || source.Type == null || source.Syntax.Node is not (FieldDeclaration or PropertyDeclaration)) return source;
        while (receiver is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
            receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner,
                ReferenceTypeExpression reference => reference.Inner, _ => receiver };
        var chain = new Stack<TypeExpression>();
        for (var part = receiver; part != null; part = part is NestedTypeExpression nested ? nested.Parent : null) chain.Push(part);
        var applied = new Dictionary<string, (TypeExpression Type, IReadOnlyList<TypeExpression> Arguments)>();
        foreach (var part in chain)
        {
            string? identity = TypeOwner(part, path, offset);
            if (identity == null)
            {
                if (SourceTypeNamespace(part, path, offset) != null) continue;
                return source;
            }
            if (SourceOwnerType(identity) is not { } declaration) return source;
            IReadOnlyList<TypeExpression> arguments = part switch {
                NamedTypeExpression named => snapshot.Checker?.GetGenericTypeOrigin(named.Name)?.TypeArguments ?? named.TypeArguments,
                NestedTypeExpression nested => nested.TypeArguments, _ => []
            };
            if (Parameters(declaration).Count != arguments.Count) return source;
            TypeExpression? normalized = declaration.Owner == null
                ? (part.IsReadOnlyValue ? part.WithoutValueProtection() : part)
                : applied.TryGetValue(declaration.Owner, out var container)
                    ? new NestedTypeExpression(container.Type, declaration.Name, arguments.ToList(), part.Line) : null;
            if (normalized == null) return source;
            applied[declaration.Qualified] = (normalized, arguments);
        }
        var bindings = new Dictionary<string, TypeExpression>();
        foreach (var ancestor in Parents(source.Syntax).Where(ancestor => IsType(ancestor.Node)).Reverse())
        {
            if (!declarations.TryGetValue(ancestor.Node, out var owner) || !applied.TryGetValue(owner.Qualified, out var actual)) continue;
            // Nested nongeneric types retain their actual enclosing application.
            foreach (var child in TypeMembers(owner.Qualified).Where(child => !child.IsTypeParameter &&
                IsType(child.Syntax.Node) && child.Owner == owner.Qualified && Parameters(child).Count == 0))
                bindings[child.Name] = new NestedTypeExpression(actual.Type, child.Name, [], child.Syntax.Node.Line);
            foreach (var pair in Parameters(owner).Zip(actual.Arguments))
                bindings[pair.First.Name] = pair.Second;
        }
        if (bindings.Count == 0) return source;
        var display = CreateDisplayCloner(bindings).CloneType(snapshot.DeclaredTypes.GetValueOrDefault(source.Syntax.Node) ?? source.Type);
        if (source.Syntax.Node is FieldDeclaration && receiver != null)
        {
            if (receiver.IsImmutableValue) display = TypeQualifiers.Immutable(display);
            else if (receiver.IsReadOnlyValue) display = TypeQualifiers.ReadOnly(display);
        }
        return source with { Type = display, BoundType = display };

        static IReadOnlyList<GenericParameter> Parameters(Symbol type) => type.Syntax.Node switch {
            ClassDeclaration declaration => declaration.GenericParameters,
            StructDeclaration declaration => declaration.GenericParameters,
            InterfaceDeclaration declaration => declaration.GenericParameters, _ => []
        };
    }
}
