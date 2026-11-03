using gflat.Preprocessing;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private MacroSymbol? MacroAt(string path, int offset) => snapshot.MacroSymbols.FirstOrDefault(symbol =>
        symbol.Span.Source != null && Workspace.Paths.Equals(symbol.Span.Source.Path, path) &&
        offset >= symbol.Span.Start && offset < symbol.Span.Start + symbol.Span.Length);

    private object? MacroHover(string path, int offset, VisualStudioHover? presentation)
    {
        if (MacroAt(path, offset) is not { } macro) return null;
        var details = new List<HoverDetail>();
        var parts = new List<DisplayPart>();
        if (macro.Classification == "preprocessor")
        {
            parts.Add(new("preprocessor keyword", macro.Signature));
            string? explanation = macro.Name switch
            {
                "define" => "Defines an object-like or function-like macro for the rest of this translation unit, including subsequent includes.",
                "undef" => "Removes a macro definition for subsequent preprocessing.",
                "include" => "Includes a file's contents here. Quoted paths search the current file's directory first, then the include directories.",
                "if" => "Includes this branch when its expanded integer expression is nonzero.",
                "ifdef" => "Includes this branch when the macro is defined.",
                "ifndef" => "Includes this branch when the macro is undefined.",
                "elif" => "Tests another expression when no earlier branch was selected.",
                "elifdef" => "Selects this branch when the macro is defined and no earlier branch was selected.",
                "elifndef" => "Selects this branch when the macro is undefined and no earlier branch was selected.",
                "else" => "Selects this branch when no earlier branch was selected.",
                "endif" => "Ends the current conditional group.",
                "pragma" => "#pragma once includes this file once per translation unit.",
                "error" => "Reports a preprocessing error with the supplied text.",
                "warning" => "Reports a preprocessing warning with the supplied text.",
                _ => null
            };
            if (explanation != null) details.Add(new("", explanation));
        }
        else
        {
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
        }
        string markdown = "```cpp\n" + macro.Signature + "\n```";
        foreach (var detail in details) markdown += "\n\n" + detail.Markdown;
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(macro.Span),
            presentation?.Render(parts.ToArray(), details.ToArray(), "constant.public"));
    }
}
