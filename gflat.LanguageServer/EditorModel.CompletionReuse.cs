using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    internal bool TryReuseIdentifierCompletion(string path, string text, int offset, out object? result)
    {
        result = null;
        if (!sources.TryGetValue(path, out var source) || offset < 0 || offset > text.Length) return false;
        int start = offset, end = offset;
        while (start > 0 && IdentifierPart(text[start - 1])) start--;
        while (end < text.Length && IdentifierPart(text[end])) end++;
        if (start < end && text[start] != '_' && !char.IsLetter(text[start])) return false;
        if ((start > 0 && text[start - 1] is '@' or '.') || (start >= 2 && text.AsSpan(start - 2, 2).SequenceEqual("::"))) return false;
        string previous = source.Text;
        if (start > previous.Length || !text.AsSpan(0, start).SequenceEqual(previous.AsSpan(0, start))) return false;
        int oldEnd = start;
        while (oldEnd < previous.Length && IdentifierPart(previous[oldEnd])) oldEnd++;
        if (!text.AsSpan(end).SequenceEqual(previous.AsSpan(oldEnd))) return false;
        var ts = tokens[path];
        int index = TokenStartIndex(ts, start);
        if (oldEnd > start)
        {
            if (index >= ts.Length || ts[index].Kind != TokenKind.Identifier || ts[index].Start != start || ts[index].End + 1 != oldEnd) return false;
            if (declarationsBySpan.ContainsKey((path, start, oldEnd - start)) || sourceTypeNames.ContainsKey(ts[index].Span)) return false;
        }
        else if (index == 0 || ts[index - 1].Kind is not (TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.Semicolon or TokenKind.Return)) return false;
        if (InCommentOrLiteral(path, start) || InDirectiveLine(previous, start) || MacroAt(path, start) != null) return false;
        var context = ContextParents(path, start).ToArray();
        if (!context.Any(parent => parent.Node is BlockStatement) ||
            !context.Any(parent => parent.Node is MethodDeclaration { IsConst: false, StaticConstructorName: null }) ||
            context.Any(parent => parent.Node is VariableDeclaration or LambdaExpression or StaticIfStatement or StaticForeachStatement or ConstructorDeclaration or PropertyDeclaration or MethodDeclaration { IsConst: true })) return false;
        // An object-like macro can change the scope when its final character is
        // typed. Let the current generation preprocess that invocation first.
        foreach (var macro in snapshot.MacroSymbols)
            if (macro.Definition && macro.Name.AsSpan().SequenceEqual(text.AsSpan(start, end - start))) return false;
        foreach (string name in snapshot.Options.Defines.Keys)
            if (name.AsSpan().SequenceEqual(text.AsSpan(start, end - start))) return false;
        var current = new SourceFile(path, text);
        result = Completion(path, start, Workspace.ToRange(current.Span(start, end - start)));
        return true;
    }

    private static bool InDirectiveLine(string text, int offset)
    {
        int start = LineStart(text, offset);
        while (true)
        {
            var prefix = text.AsSpan(start, offset - start).TrimStart();
            if (!prefix.IsEmpty && prefix[0] == '#') return true;
            if (start == 0) return false;
            int previousEnd = start - 1;
            int previousStart = PreviousLineStart(text, start);
            var previous = text.AsSpan(previousStart, previousEnd - previousStart).TrimEnd();
            if (previous.IsEmpty || previous[^1] != '\\') return false;
            start = previousStart;
        }
    }

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
        int separatorIndex = TokenStartIndex(ts, start) - 1;
        var separator = separatorIndex >= 0 && ts[separatorIndex].End == start - 1 &&
            ts[separatorIndex].Kind is TokenKind.Dot or TokenKind.DoubleColon ? ts[separatorIndex] : null;
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
        var ts = tokens[path];
        int leftIndex = TokenStartIndex(ts, separatorStart) - 1;
        if (leftIndex < 0 || ts[leftIndex].End != separatorStart - 1 || InCommentOrLiteral(path, separatorStart) ||
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
