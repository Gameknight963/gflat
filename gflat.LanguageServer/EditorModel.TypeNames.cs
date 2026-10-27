using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<SourceSpan, TypeExpression> sourceTypeNames = [];

    private string? SourceTypeNamespace(TypeExpression type, string path, int offset)
    {
        string qualifier = DisplayType(type.WithoutValueProtection());
        bool globalQualifier = qualifier.StartsWith("global::", StringComparison.Ordinal);
        if (globalQualifier) qualifier = qualifier[8..];
        string head = qualifier.Split("::")[0];
        if (!globalQualifier && Visible(path, offset, head).Any(symbol =>
            symbol.IsTypeParameter || IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration)) return null;
        return snapshot.Checker?.ResolveVisibleNamespacePath(qualifier,
            globalQualifier ? "" : ScopeAt(path, offset).Namespace,
            globalQualifier ? [] : snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? []);
    }

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
            (s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration));
        if (lexical != null && (lexical.IsTypeParameter || lexical.Syntax.Node is AliasDeclaration)) return lexical;
        if (snapshot.Checker?.GetSourceTypeBinding(source) is NamedTypeExpression bound &&
            snapshot.Checker.GetTypeDeclaration(bound.Name) is { } declaration &&
            sourceTypeDeclarations.TryGetValue((declaration.GetType(), declaration.Span), out var symbol))
            return source is NestedTypeExpression ? symbol with { BoundType = bound } : symbol;
        // Generic bodies need not have been specialized. Resolve the source
        // type's containing scope instead of treating the token after `>::`
        // as an unqualified name (or a constructor call).
        if (source is not NestedTypeExpression) return null;
        string? owner = TypeOwner(source, token.Span.Source!.Path, token.Start);
        return owner == null ? null : symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == owner);
    }
}
