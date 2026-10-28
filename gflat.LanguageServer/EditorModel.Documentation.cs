using System.Text;
using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<SourceSpan, string> declarationDocumentation = [];

    private string? Documentation(Symbol symbol) => symbol.IsTypeParameter ? null : declarationDocumentation.GetValueOrDefault(symbol.Syntax.Span);

    private void IndexDocumentation()
    {
        // Source projections retain Syntax. Index its captured declaration span,
        // never a specialized display name or a rewritten AST node's span.
        var declarations = symbols.Where(symbol => !symbol.IsTypeParameter && symbol.Scope == null &&
            symbol.Syntax.Node is ClassDeclaration or StructDeclaration or InterfaceDeclaration or EnumDeclaration or
                MethodDeclaration or ConstructorDeclaration or ExternDeclaration or FieldDeclaration or PropertyDeclaration or AliasDeclaration or EnumMemberDeclaration);
        var syntaxErrors = snapshot.Diagnostics.Where(diagnostic => diagnostic.Descriptor.Category == "Syntax")
            .Select(diagnostic => diagnostic.Span).ToArray();
        foreach (var group in declarations.Where(symbol => symbol.Syntax.Span.Source is { } source && !source.Path.StartsWith('<'))
            .GroupBy(symbol => symbol.Syntax.Span.Source!.Path, Workspace.Paths))
        {
            var fileTokens = tokens[group.Key];
            int index = 0;
            foreach (var symbol in group.OrderBy(symbol => symbol.Syntax.Span.Start))
            {
                var span = symbol.Syntax.Span;
                while (index < fileTokens.Length && fileTokens[index].Start < span.Start) index++;
                if (index == fileTokens.Length || fileTokens[index].Start != span.Start || symbol.NameSpan.Length == 0 ||
                    symbol.NameSpan.Source != span.Source || symbol.NameSpan.Start < span.Start || symbol.NameSpan.Start >= span.Start + span.Length) continue;
                if (syntaxErrors.Any(error => error.Source == span.Source && error.Start >= span.Start && error.Start <= span.Start + span.Length)) continue;
                string? documentation = ReadDocumentation(span.Source!, index == 0 ? 0 : fileTokens[index - 1].End + 1, span.Start);
                if (documentation != null) declarationDocumentation.TryAdd(span, documentation);
            }
        }
    }

    private static string? ReadDocumentation(SourceFile source, int gapStart, int declarationStart)
    {
        string text = source.Text;
        static int LineStart(string text, int at) => at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
        int declarationLine = LineStart(text, declarationStart);
        if (!text.AsSpan(declarationLine, declarationStart - declarationLine).Trim().IsEmpty) return null;
        var lines = new Dictionary<int, string>();
        // The existing token stream bounds a trivia-only gap. Recognize its
        // comments in lexer order so /// inside a block comment cannot attach.
        for (int at = gapStart; at < declarationStart;)
        {
            if (char.IsWhiteSpace(text[at])) { at++; continue; }
            if (at + 1 >= declarationStart || text[at] != '/') return null;
            if (text[at + 1] == '/')
            {
                int end = text.IndexOf('\n', at);
                if (end < 0 || end > declarationStart) end = declarationStart;
                int line = LineStart(text, at);
                if (at + 2 < end && text[at + 2] == '/' && (at + 3 == end || text[at + 3] != '/') &&
                    text.AsSpan(line, at - line).Trim().IsEmpty)
                {
                    int body = at + 3;
                    if (body < end && text[body] == ' ') body++;
                    lines[line] = text[body..end].TrimEnd();
                }
                at = end;
            }
            else if (text[at + 1] == '*')
            {
                int end = text.IndexOf("*/", at + 2, StringComparison.Ordinal);
                if (end < 0 || end + 2 > declarationStart) return null;
                at = end + 2;
            }
            else return null;
        }
        var content = new List<string>();
        for (int line = declarationLine; line > gapStart;)
        {
            line = LineStart(text, line - 1);
            if (!lines.TryGetValue(line, out var body)) break;
            content.Add(body);
        }
        content.Reverse();
        string result = string.Join('\n', content).Trim('\n');
        return result.Length == 0 ? null : result;
    }

    private static string DocumentationMarkdown(string prose)
    {
        // Treat prose literally in Markdown; rich VS rows and completion carry
        // the original plaintext. No XML, links or formatting are interpreted.
        var result = new StringBuilder(prose.Length);
        foreach (char value in prose)
        {
            if (value is >= '!' and <= '/' or >= ':' and <= '@' or >= '[' and <= '`' or >= '{' and <= '~') result.Append('\\');
            result.Append(value);
        }
        return result.ToString();
    }
}
