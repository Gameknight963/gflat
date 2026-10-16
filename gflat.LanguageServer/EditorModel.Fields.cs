using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<Token, Symbol> boundFieldReferences = new(ReferenceEqualityComparer.Instance);

    private void IndexBoundFields()
    {
        if (snapshot.Checker is not { } checker) return;
        var fields = symbols.Where(symbol => symbol.Syntax.Node is FieldDeclaration)
            .ToDictionary(symbol => (symbol.Syntax.Span, symbol.Name));
        if (fields.Count == 0) return;
        foreach (var syntax in snapshot.Nodes)
        {
            var site = syntax.Node;
            if (site is not (IdentifierExpression or MemberAccessExpression or NamespaceAccessExpression)) continue;
            if (TypeChecker.TypeName(checker.GetType(site)) == "<error>" || checker.GetPropertyRead(site) != null) continue;
            FieldDeclaration? field = checker.GetStaticStorage(site);
            string? owner = field == null ? null : checker.GetStaticStorageInfo(field).Owner;
            if (field == null && site is MemberAccessExpression member)
            {
                TypeExpression receiver = checker.GetType(member.Object);
                while (receiver is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
                    receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner, ReferenceTypeExpression reference => reference.Inner, _ => receiver };
                if (receiver is not NamedTypeExpression named) continue;
                if (checker.GetStructByIdentity(named.Name) is { } structure)
                {
                    field = structure.FieldDeclarationsByName.GetValueOrDefault(member.Member);
                    owner = structure.Name;
                }
                else if (checker.GetClassByIdentity(named.Name) is { } cls)
                {
                    field = cls.FieldDeclarationsByName.GetValueOrDefault(member.Member);
                    owner = cls.Fields.FirstOrDefault(candidate => candidate.Name == member.Member).DeclaringClass;
                }
            }
            if (field == null || !fields.TryGetValue((field.Span, field.Name), out var source) ||
                site.Span.Source?.Path is not { } path || !tokens.TryGetValue(path, out var fileTokens)) continue;
            int start = site switch { MemberAccessExpression access => access.Object.Span.Start + access.Object.Span.Length,
                NamespaceAccessExpression access => access.Left.Span.Start + access.Left.Span.Length, IdentifierExpression => site.Span.Start, _ => -1 };
            if (start < 0) continue;
            var token = fileTokens.FirstOrDefault(token => token.Kind == TokenKind.Identifier && token.Text == field.Name && token.Start >= start && token.End < site.Span.Start + site.Span.Length);
            if (token == null || !Accessible(source, path, token.Start)) continue;
            var declared = snapshot.DeclaredTypes.GetValueOrDefault(source.Syntax.Node) ?? ((FieldDeclaration)source.Syntax.Node).Type;
            var names = DisplayGenericNames(source);
            var bindings = new Dictionary<string, TypeExpression>();
            if (owner != null) BindDisplayOwnerArguments(source, owner, bindings);
            InferDisplayBindings(declared, field.Type, names, bindings);
            var display = new AstCloner(bindings).CloneType(declared);
            // Nested owners may not carry all enclosing generic arguments. Use
            // the checked declaration type if its source spelling cannot be bound.
            if (HasUnboundDisplayParameter(display, names)) display = field.Type;
            boundFieldReferences[token] = source with {
                Owner = owner == null ? source.Owner : checker.DisplayName(owner),
                Type = display,
                BoundType = display
            };
        }
    }
}
