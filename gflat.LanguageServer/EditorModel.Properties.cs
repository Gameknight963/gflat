using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<Token, Symbol> boundPropertyReferences = new(ReferenceEqualityComparer.Instance);

    private void IndexBoundProperties()
    {
        if (snapshot.Checker is not { } checker) return;
        var accessors = symbols.Where(s => s.Syntax.Node is PropertyDeclaration).SelectMany(symbol => {
            var property = (PropertyDeclaration)symbol.Syntax.Node;
            return new[] { property.Getter, property.Setter }.OfType<MethodDeclaration>().Select(accessor => (accessor.Span, Symbol: symbol));
        }).GroupBy(entry => entry.Span).ToDictionary(group => group.Key, group => group.First().Symbol);
        foreach (var syntax in snapshot.Nodes)
        {
            AstNode site = syntax.Node;
            var call = checker.GetPropertyRead(site);
            if (checker.GetPropertyWrite(site) is { } write)
            {
                call = write.Setter;
                site = site switch { AssignmentExpression assignment => assignment.Target, UnaryExpression unary => unary.Operand, _ => site };
            }
            if (call == null || checker.GetResolvedCall(call) is not MethodDeclaration target ||
                !accessors.TryGetValue(target.Span, out var source) || target.PropertyName != source.Name ||
                site.Span.Source?.Path is not { } path || !tokens.TryGetValue(path, out var fileTokens)) continue;
            string? name = site switch { IdentifierExpression id => id.Name, MemberAccessExpression member => member.Member, NamespaceAccessExpression member => member.Member, _ => null };
            if (name == null) continue; // Indexers have no named property token at the use site.
            int start = site switch { MemberAccessExpression member => member.Object.Span.Start + member.Object.Span.Length,
                NamespaceAccessExpression member => member.Left.Span.Start + member.Left.Span.Length, _ => site.Span.Start };
            var token = fileTokens.FirstOrDefault(token => token.Kind == TokenKind.Identifier && token.Text == name && token.Start >= start && token.End < site.Span.Start + site.Span.Length);
            var actual = checker.GetType(site);
            if (token == null || TypeChecker.TypeName(actual) == "<error>") continue;
            string? owner = source.Owner;
            TypeExpression display = actual;
            if (checker.TryGetMethodOwner(target, out string identity, out _))
            {
                owner = checker.DisplayName(identity);
                var members = checker.GetTypeDeclaration(identity) switch { ClassDeclaration cls => cls.Members, StructDeclaration str => str.Members, InterfaceDeclaration iface => iface.Members, _ => null };
                // Concrete properties carry substituted source types, including
                // alias spelling. Ref access through a readonly receiver can add
                // a qualification beyond that declared type. Compare the checked
                // mutable accessor first: an alias may already mean readonly(T).
                if (members?.OfType<PropertyDeclaration>().FirstOrDefault(property => property.Name == source.Name && property.Span == source.Syntax.Span) is { } property)
                {
                    display = property.Type;
                    if (target.IsRefReturn && target.IsReadOnly)
                    {
                        var mutable = members.OfType<MethodDeclaration>().FirstOrDefault(method => method.PropertyName == property.Name &&
                            method.IsRefReturn && !method.IsReadOnly && method.Span == target.Span);
                        if (mutable?.ReturnType is PointerTypeExpression pointer)
                        {
                            if (actual.IsReadOnlyValue && !pointer.Inner.IsReadOnlyValue)
                                display = TypeQualifiers.ReadOnly(display);
                        }
                        else display = actual;
                    }
                }
            }
            else display = snapshot.DeclaredTypes.GetValueOrDefault(source.Syntax.Node) ?? actual;
            boundPropertyReferences[token] = source with { Owner = owner, Type = display, BoundMethod = target };
        }
    }
}
