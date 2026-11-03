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
        int name = macro.Signature.StartsWith("#define ", StringComparison.Ordinal) ? 8 : 0;
        if (name != 0) parts.Add(new("preprocessor keyword", macro.Signature[..name]));
        parts.Add(new("cppMacro", macro.Name));
        string tail = macro.Signature[(name + macro.Name.Length)..];
        int previous = 0;
        foreach (var token in Lexer.Tokenize(new SourceFile("<macro-hover>", tail), new diagnostics.DiagnosticBag()))
        {
            if (token.Start >= tail.Length || !(char.IsAsciiLetter(tail[token.Start]) || tail[token.Start] == '_') ||
                token.Start > 0 && tail[token.Start - 1] == '@') continue;
            string? kind = (macro.Parameters ?? []).Contains(token.Text) ? "parameter name" :
                (macro.ReferencedMacros ?? []).Contains(token.Text) ? "cppMacro" : null;
            if (kind == null) continue;
            parts.Add(new("text", tail[previous..token.Start]));
            parts.Add(new(kind, token.Text));
            previous = token.Start + token.Span.Length;
        }
        parts.Add(new("text", tail[previous..]));
        if (macro.Expansion != null) details.Add(new("Expands to", macro.Expansion.Length == 0 ? "(empty)" : macro.Expansion));
        string markdown = "```cpp\n" + macro.Signature + "\n```";
        foreach (var detail in details) markdown += "\n\n" + detail.Markdown;
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(macro.Span),
            presentation?.Render(parts.ToArray(), details.ToArray(), "constant.public"));
    }
}
