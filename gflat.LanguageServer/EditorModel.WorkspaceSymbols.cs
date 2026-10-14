using gflat.ast;

namespace gflat.LanguageServer;

public sealed record WorkspaceSymbolInformation(string Name, int Kind, Location Location, string? ContainerName);

public sealed partial class EditorModel
{
    private WorkspaceSymbolInformation[]? workspaceSymbols;

    public WorkspaceSymbolInformation[] WorkspaceSymbols(string query)
    {
        // Search declarations in the analyzed project, including unopened files.
        // Keep source overloads distinct; specializations and generated accessors
        // are absent from this source declaration index.
        workspaceSymbols ??= symbols.Select(symbol => (Symbol: symbol, Kind: OutlineKind(symbol.Syntax.Node)))
            .Where(entry => !entry.Symbol.IsTypeParameter && entry.Kind != 0 && entry.Symbol.NameSpan.Source is { } source &&
                !source.Path.StartsWith('<') && entry.Symbol.NameSpan.Length > 0)
            .Select(entry => new WorkspaceSymbolInformation(entry.Symbol.Name, entry.Kind,
                new Location(Workspace.FileUri(entry.Symbol.NameSpan.Source!.Path), Workspace.ToRange(entry.Symbol.NameSpan)),
                entry.Symbol.Owner ?? (entry.Symbol.Namespace.Length == 0 ? null : entry.Symbol.Namespace)))
            .Concat(snapshot.Nodes.Where(syntax => syntax.Node is DestructorDeclaration or OperatorDeclaration &&
                    syntax.Span.Source is { } source && !source.Path.StartsWith('<'))
                .Select(syntax => {
                    string owner = Owner(Parents(syntax)) ?? Namespace(Parents(syntax));
                    string typeName = Parents(syntax).Where(parent => IsType(parent.Node))
                        .Select(parent => SourceName(parent.Node)).FirstOrDefault() ?? "";
                    string name = syntax.Node is OperatorDeclaration op ? "operator " + op.OperatorSymbol : "~" + typeName;
                    var selection = NameSpan(syntax, syntax.Node is OperatorDeclaration ? "operator" : typeName, null);
                    return new WorkspaceSymbolInformation(name, OutlineKind(syntax.Node),
                        new Location(Workspace.FileUri(selection.Source!.Path), Workspace.ToRange(selection)), owner);
                }))
            .Distinct().OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(symbol => symbol.ContainerName, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Location.Uri, StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Location.Range.Start.Line).ThenBy(symbol => symbol.Location.Range.Start.Character)
            .ToArray();
        query = query.Trim();
        return workspaceSymbols.Where(symbol => (symbol.ContainerName == null ? symbol.Name :
                symbol.ContainerName + "::" + symbol.Name).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
}
