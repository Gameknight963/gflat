using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private IEnumerable<Symbol> SourceGenericStaticMethods(TypeExpression qualifier, string path, int offset, string? name)
    {
        // A source-only type application must not unwrap a provider alias or
        // constrained type parameter and expose its implementation's members.
        for (TypeExpression? part = qualifier; part != null; part = part is NestedTypeExpression nested ? nested.Parent : null)
        {
            Symbol? declaration = null;
            if (part is NamedTypeExpression named)
                declaration = Visible(path, offset, named.Name).FirstOrDefault(symbol => symbol.Name == named.Name &&
                    (symbol.IsTypeParameter || IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration));
            else if (part is NestedTypeExpression member)
            {
                string? parent = TypeOwner(member.Parent, path, offset) ?? SourceTypeNamespace(member.Parent, path, offset);
                if (parent != null) declaration = symbols.FirstOrDefault(symbol => symbol.Qualified == Join(parent, member.Member) &&
                    (IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration));
            }
            if (declaration != null && (declaration.IsTypeParameter || declaration.Syntax.Node is AliasDeclaration || !Accessible(declaration, path, offset))) return [];
        }
        string? owner = TypeOwner(qualifier, path, offset);
        if (owner == null) return [];
        return TypeMembers(owner).Where(symbol => symbol.Owner == owner && symbol.Static &&
                symbol.Syntax.Node is MethodDeclaration { StaticConstructorName: null } && (name == null || symbol.Name == name) && Accessible(symbol, path, offset))
            .GroupBy(symbol => symbol.Name).Where(family => family.Count() == 1)
            .Select(family => ProjectSourceMemberType(family.Single(), qualifier, path, offset)).ToArray();
    }

    private Symbol ProjectSourceMemberType(Symbol source, TypeExpression? receiver, string path, int offset)
    {
        bool staticMethod = source.Static && source.Syntax.Node is MethodDeclaration;
        if (source.Type == null || !staticMethod && (source.Static || source.Syntax.Node is not (FieldDeclaration or PropertyDeclaration))) return source;
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
        if (source.Syntax.Node is MethodDeclaration method)
            foreach (var parameter in method.GenericParameters) bindings.Remove(parameter.Name);
        if (bindings.Count == 0 && !staticMethod) return source;
        var cloner = CreateDisplayCloner(bindings);
        var display = cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(source.Syntax.Node) ?? source.Type);
        if (source.Syntax.Node is MethodDeclaration callable)
            return source with {
                Type = display,
                BoundParameters = callable.Parameters.Select(parameter => new Parameter(parameter.Name,
                    cloner.CloneType(snapshot.DeclaredTypes.GetValueOrDefault(parameter) ?? parameter.Type), parameter.Line,
                    parameter.IsConst, parameter.DefaultValue) { Span = parameter.Span, IsExtensionReceiver = parameter.IsExtensionReceiver }).ToArray(),
                Owner = source.Owner != null && applied.TryGetValue(source.Owner, out var actualOwner) ? DisplayType(actualOwner.Type) : source.Owner,
                GenericBindings = bindings
            };
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
