using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<SourceSpan, TypeExpression> sourceTypeNames = [];

    private void IndexSourceTypeNames()
    {
        foreach (var syntax in snapshot.Nodes)
        {
            var span = syntax.Node switch {
                NamedTypeExpression named => named.NameSpan,
                NestedTypeExpression nested => nested.NameSpan,
                _ => default
            };
            if (span.Source != null && span.Length > 0)
                sourceTypeNames.TryAdd(span, (TypeExpression)syntax.Node);
        }
    }

    private Symbol? BoundTypeName(Token token, IEnumerable<Symbol> candidates)
    {
        if (!sourceTypeNames.TryGetValue(token.Span, out var source)) return null;
        // An alias and a type parameter name denote their own declarations,
        // even when checking has resolved the use to a concrete implementation.
        var lexical = candidates.FirstOrDefault(s => s.Name == token.Text &&
            (s.IsTypeParameter || s.Syntax.Node is AliasDeclaration));
        if (lexical != null) return lexical;
        if (snapshot.Checker?.GetSourceTypeBinding(source) is NamedTypeExpression bound &&
            snapshot.Checker.GetTypeDeclaration(bound.Name) is { } declaration &&
            sourceTypeDeclarations.TryGetValue((declaration.GetType(), declaration.Span), out var symbol))
            return symbol;
        // Generic bodies need not have been specialized. Resolve the source
        // type's containing scope instead of treating the token after `>::`
        // as an unqualified name (or a constructor call).
        if (source is not NestedTypeExpression) return null;
        string? owner = TypeOwner(source, token.Span.Source!.Path, token.Start);
        return owner == null ? null : symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == owner);
    }
}
