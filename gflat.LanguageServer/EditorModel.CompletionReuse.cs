using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    // Editing only the member name leaves the checked receiver and scope intact.
    // All other edits go through the current generation's analysis.
    internal bool TryReuseMemberCompletion(string path, string text, int offset, out object? result)
    {
        result = null;
        if (!sources.TryGetValue(path, out var source) || offset < 0 || offset > text.Length) return false;
        int start = offset, end = offset;
        while (start > 0 && IdentifierPart(text[start - 1])) start--;
        while (end < text.Length && IdentifierPart(text[end])) end++;
        if (start == 0 || text[start - 1] != '.' && !(start >= 2 && text.AsSpan(start - 2, 2).SequenceEqual("::"))) return false;
        string previous = source.Text;
        if (start > previous.Length || !text.AsSpan(0, start).SequenceEqual(previous.AsSpan(0, start)))
            return TryInsertedMemberSeparator(path, text, start, end, out result);
        int oldEnd = start;
        while (oldEnd < previous.Length && IdentifierPart(previous[oldEnd])) oldEnd++;
        if (!text.AsSpan(end).SequenceEqual(previous.AsSpan(oldEnd))) return false;
        var ts = tokens[path];
        var separator = ts.FirstOrDefault(token => token.End == start - 1 && token.Kind is TokenKind.Dot or TokenKind.DoubleColon);
        if (separator == null || InCommentOrLiteral(path, start)) return false;
        if (!ContextParents(path, start).Any(parent => parent.Node is BlockStatement)) return false;
        var current = new SourceFile(path, text);
        result = Completion(path, start, Workspace.ToRange(current.Span(start, end - start)));
        return true;
    }

    private bool TryInsertedMemberSeparator(string path, string text, int start, int end, out object? result)
    {
        result = null;
        int separatorStart = start - 1;
        string previous = sources[path].Text;
        if (text[separatorStart] != '.' || separatorStart > previous.Length ||
            !text.AsSpan(0, separatorStart).SequenceEqual(previous.AsSpan(0, separatorStart)) ||
            !text.AsSpan(end).SequenceEqual(previous.AsSpan(separatorStart))) return false;
        int leftIndex = Array.FindIndex(tokens[path], token => token.End == separatorStart - 1);
        if (leftIndex < 0 || InCommentOrLiteral(path, separatorStart) ||
            !ContextParents(path, separatorStart).Any(parent => parent.Node is BlockStatement)) return false;
        var current = new SourceFile(path, text);
        var range = Workspace.ToRange(current.Span(start, end - start));
        if (ReflectionCompletion(path, separatorStart, leftIndex + 2, range, insertedSeparator: true) is { } reflected)
        {
            result = reflected;
            return true;
        }
        var separator = new Token(TokenKind.Dot, 0, separatorStart, separatorStart);
        var items = Members(path, separatorStart, leftIndex + 1, insertedSeparator: separator)
            .Where(symbol => symbol.Syntax.Node is not ConstructorDeclaration)
            .DistinctBy(symbol => (symbol.Name, symbol.BoundExtension))
            .Select(symbol => new {
                label = symbol.Name, kind = symbol.Kind, detail = Detail(symbol),
                documentation = Documentation(symbol) is { } prose ? new Markup("plaintext", prose) : null,
                textEdit = new { range, newText = CompletionSpelling(symbol) }
            }).ToArray();
        result = new { isIncomplete = false, items };
        return true;
    }

    private static bool IdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);
}
