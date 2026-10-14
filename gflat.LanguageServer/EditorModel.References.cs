namespace gflat.LanguageServer;

public sealed record DocumentHighlight(TextRange Range);

public sealed partial class EditorModel
{
    private Dictionary<SourceSpan, SourceSpan[]>? referenceIndex;

    public DocumentHighlight[] DocumentHighlights(string path, int offset)
    {
        string uri = Workspace.FileUri(path);
        return References(path, offset, includeDeclaration: true)
            .Where(location => location.Uri == uri)
            .Select(location => new DocumentHighlight(location.Range)).ToArray();
    }

    public Location[] References(string path, int offset, bool includeDeclaration)
    {
        int index = TokenAt(path, offset);
        var symbol = index < 0 ? null : Resolve(path, index);
        if (symbol?.NameSpan.Source is not { } source || source.Path.StartsWith('<')) return [];

        // Reuse the same resolved occurrences as highlighting and navigation.
        // Index lazily: snapshots that never receive this query incur no cost.
        referenceIndex ??= references.Where(pair => pair.Value?.NameSpan.Source is { } definition &&
                !definition.Path.StartsWith('<') && pair.Key.Span.Source is { } occurrence && !occurrence.Path.StartsWith('<'))
            .GroupBy(pair => pair.Value!.NameSpan)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key.Span).Distinct().ToArray());
        var spans = referenceIndex.GetValueOrDefault(symbol.NameSpan) ?? [];
        if (includeDeclaration) spans = [.. spans, symbol.NameSpan];
        return spans.Distinct().Where(span => includeDeclaration || span != symbol.NameSpan)
            .OrderBy(span => span.Source!.Path, Workspace.Paths).ThenBy(span => span.Start)
            .Select(span => new Location(Workspace.FileUri(span.Source!.Path), Workspace.ToRange(span))).ToArray();
    }
}
