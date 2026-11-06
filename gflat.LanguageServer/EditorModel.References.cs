namespace gflat.LanguageServer;

public sealed record DocumentHighlight(TextRange Range);

public sealed partial class EditorModel
{
    private Dictionary<SourceSpan, SourceSpan[]>? referenceIndex;

    public DocumentHighlight[] DocumentHighlights(string path, int offset)
    {
        if (!sources.TryGetValue(path, out var source)) return [];
        path = source.Path;
        int index = TokenAt(source.Path, offset);
        var symbol = DocumentationReferenceAt(path, offset)?.Symbol ?? (index < 0 ? null : Resolve(source.Path, index));
        if (symbol?.NameSpan.Source is not { } declaration || declaration.Path.StartsWith('<')) return [];
        var spans = new HashSet<SourceSpan>();
        var fileTokens = tokens[source.Path];
        for (int occurrence = 0; occurrence < fileTokens.Length; occurrence++)
            if (Resolve(source.Path, occurrence)?.NameSpan == symbol.NameSpan) spans.Add(fileTokens[occurrence].Span);
        if (Workspace.Paths.Equals(declaration.Path, source.Path)) spans.Add(symbol.NameSpan);
        foreach (var pair in documentationReferences)
            if (pair.Value.NameSpan == symbol.NameSpan && Workspace.Paths.Equals(pair.Key.Source?.Path, path)) spans.Add(pair.Key);
        return spans.OrderBy(span => span.Start)
            .Select(span => new DocumentHighlight(Workspace.ToRange(span))).ToArray();
    }

    public Location[] References(string path, int offset, bool includeDeclaration)
    {
        path = CanonicalQueryPath(path);
        int index = TokenAt(path, offset);
        var symbol = DocumentationReferenceAt(path, offset)?.Symbol ?? (index < 0 ? null : Resolve(path, index));
        if (symbol?.NameSpan.Source is not { } source || source.Path.StartsWith('<')) return [];

        // References include unopened files, even when only one document has
        // requested highlighting or navigation so far.
        if (referenceIndex == null)
        {
            EnsureReferences();
            referenceIndex = references.Where(pair => pair.Value?.NameSpan.Source is { } definition &&
                !definition.Path.StartsWith('<') && pair.Key.Span.Source is { } occurrence && !occurrence.Path.StartsWith('<'))
                .GroupBy(pair => pair.Value!.NameSpan)
                .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key.Span).Distinct().ToArray());
        }
        var spans = (referenceIndex.GetValueOrDefault(symbol.NameSpan) ?? [])
            .Concat(documentationReferences.Where(pair => pair.Value.NameSpan == symbol.NameSpan).Select(pair => pair.Key)).ToArray();
        if (includeDeclaration) spans = [.. spans, symbol.NameSpan];
        return spans.Distinct().Where(span => includeDeclaration || span != symbol.NameSpan)
            .OrderBy(span => span.Source!.Path, Workspace.Paths).ThenBy(span => span.Start)
            .Select(span => new Location(Workspace.FileUri(span.Source!.Path), Workspace.ToRange(span))).ToArray();
    }
}
