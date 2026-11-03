using gflat.Preprocessing;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private MacroSymbol? MacroAt(string path, int offset) => snapshot.MacroSymbols.FirstOrDefault(symbol =>
        symbol.Span.Source != null && Workspace.Paths.Equals(symbol.Span.Source.Path, path) &&
        offset >= symbol.Span.Start && offset < symbol.Span.Start + symbol.Span.Length);

    private object? MacroHover(string path, int offset, VisualStudioHover? presentation)
    {
        if (MacroAt(path, offset) is not { Classification: "macro" } macro) return null;
        var details = new List<HoverDetail>();
        var parts = new List<DisplayPart>();
        parts.AddRange(MacroDisplayParts(macro.DeclarationTokens ?? [new(macro.Signature, "macro")]));
        if (macro.Expansion != null)
            details.Add(new("Expands to", macro.Expansion.Length == 0 ? "(empty)" : macro.Expansion,
                macro.Expansion.Length == 0 ? [new("text", "(empty)")] : MacroDisplayParts(macro.ExpansionTokens ?? [], VisualStudioHover.UseClassificationFont)));
        string markdown = "```cpp\n" + macro.Signature + "\n```";
        foreach (var detail in details) markdown += "\n\n" + detail.Markdown;
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(macro.Span),
            presentation?.Render(parts.ToArray(), details.ToArray(), "constant.public"));
    }
    private static DisplayPart[] MacroDisplayParts(IReadOnlyList<MacroToken> tokens, int style = VisualStudioHover.UseClassificationStyle)
        => tokens.Select(token => new DisplayPart(
            Array.IndexOf(TokenTypes, token.Classification) is int index && index >= 0
                ? VisualStudioTokenTypes[index] : "text", token.Text, ClassificationStyle: style)).ToArray();

    private (SourceSpan Span, int Kind, int Modifiers)[] MacroSourceClassifications(string path)
    {
        var result = new Dictionary<(int Start, int Length), (SourceSpan Span, int Kind, int Modifiers)>();
        void Add(SourceSpan span, string classification, int modifiers = 0)
        {
            if (span.Source is not { } source || !Workspace.Paths.Equals(source.Path, path)) return;
            int kind = Array.IndexOf(TokenTypes, classification);
            if (kind < 0) return;
            int end = span.Start + span.Length;
            for (int start = span.Start; start < end;)
            {
                int lineEnd = source.Text.IndexOfAny(['\r', '\n'], start, end - start);
                if (lineEnd < 0) lineEnd = end;
                if (lineEnd > start)
                {
                    var physical = source.Span(start, lineEnd - start);
                    result.TryAdd((physical.Start, physical.Length), (physical, kind, modifiers));
                }
                start = lineEnd;
                while (start < end && source.Text[start] is '\r' or '\n') start++;
            }
        }
        foreach (var symbol in snapshot.MacroSymbols)
        {
            Add(symbol.Span, symbol.Classification, symbol.Definition ? 1 : 0);
            if (symbol.Definition && symbol.Span.Source is { } declarationSource && Workspace.Paths.Equals(declarationSource.Path, path))
                foreach (var token in symbol.DeclarationTokens ?? []) Add(token.Span, token.Classification);
        }
        return result.Values.OrderBy(item => item.Span.Start).ToArray();
    }
}
