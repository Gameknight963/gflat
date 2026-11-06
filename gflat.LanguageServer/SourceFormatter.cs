using gflat.ast;

namespace gflat.LanguageServer;

public sealed record FormattingOptions(int TabSize = 4, bool InsertSpaces = true, bool BracesOnNewLine = true);
public sealed record SourceEdit(TextRange Range, string NewText);

public static class SourceFormatter
{
    public static SourceEdit[] Format(SourceFile source, FormattingOptions options, TextRange? range = null,
        IReadOnlyList<AnalysisSnapshot.Syntax>? syntax = null)
    {
        if (options.TabSize is < 1 or > 16) throw new ArgumentException("Tab size must be between 1 and 16.");
        string text = source.Text;
        string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        syntax ??= new AnalysisSnapshot([source], new() { NoStdLib = true }, checkTypes: false).Nodes;
        var typeSpans = syntax.Where(n => n.Node is TypeExpression && n.Span.Source?.Path == source.Path).Select(n => n.Span).ToArray();
        var orderedTypes = typeSpans.OrderBy(span => span.Start).ToArray();
        var typeStarts = orderedTypes.Select(span => span.Start).ToArray();
        var typeEnds = new int[orderedTypes.Length];
        for (int i = 0; i < orderedTypes.Length; i++) typeEnds[i] = Math.Max(i == 0 ? 0 : typeEnds[i - 1], orderedTypes[i].Start + orderedTypes[i].Length);
        bool InSameType(int start, int end)
        {
            int i = Array.BinarySearch(typeStarts, start);
            if (i < 0) i = ~i - 1;
            else while (i + 1 < typeStarts.Length && typeStarts[i + 1] == start) i++;
            return i >= 0 && typeEnds[i] > end;
        }
        var tokens = Lexer.Tokenize(source, new diagnostics.DiagnosticBag()).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        var tokenStarts = tokens.Select(token => token.Start).ToArray();
        var unaryMinus = new HashSet<int>();
        var binaryOperators = new HashSet<int>();
        foreach (var node in syntax)
        {
            if (node.Node is UnaryExpression { IsPrefix: true, Operator: TokenKind.Minus } unary &&
                unary.Operand.Span.Source?.Path == source.Path)
            {
                int index = Array.BinarySearch(tokenStarts, unary.Operand.Span.Start);
                index = index < 0 ? ~index - 1 : index - 1;
                if (index >= 0 && tokens[index].Kind == TokenKind.Minus) unaryMinus.Add(tokens[index].Start);
            }
            if (node.Node is BinaryExpression binary && binary.Left.Span.Source?.Path == source.Path &&
                binary.Right.Span.Source?.Path == source.Path)
            {
                int index = Array.BinarySearch(tokenStarts, binary.Left.Span.Start + binary.Left.Span.Length);
                if (index < 0) index = ~index;
                for (; index < tokens.Length && tokens[index].Start < binary.Right.Span.Start; index++)
                    if (tokens[index].Kind == binary.Operator)
                    {
                        binaryOperators.Add(tokens[index].Start);
                        break;
                    }
            }
        }
        var directives = new HashSet<int>();
        bool continued = false;
        int lineNumber = 1;
        foreach (string line in text.Split('\n'))
        {
            bool directive = continued || line.TrimStart().StartsWith('#');
            if (directive) directives.Add(lineNumber);
            continued = directive && line.TrimEnd('\r', ' ', '\t').EndsWith('\\');
            lineNumber++;
        }
        var edits = new List<SourceEdit>();
        int braces = 0, parens = 0, interpolation = 0;
        bool usingLine = false;
        Token? previous = null;
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++) if (text[i] == '\n') lineStarts.Add(i + 1);
        int Offset(Position position)
        {
            if (position.Line < 0 || position.Character < 0) throw new ArgumentException("Formatting positions cannot be negative.");
            if (position.Line >= lineStarts.Count) return text.Length;
            int at = lineStarts[position.Line];
            int end = text.IndexOfAny(['\r', '\n'], at);
            if (end < 0) end = text.Length;
            return at + Math.Min(position.Character, end - at);
        }
        int lower = range == null ? 0 : Offset(range.Start), upper = range == null ? text.Length : Offset(range.End);
        string Indent(int depth) => string.Concat(Enumerable.Repeat(options.InsertSpaces ? new string(' ', options.TabSize) : "\t", Math.Max(0, depth)));
        void Edit(int start, int length, string replacement)
        {
            if (start < lower && start + length >= lower && replacement.Contains('\n') &&
                !text.AsSpan(lower, start + length - lower).Contains('\n'))
            {
                length -= lower - start;
                start = lower;
                replacement = replacement[(replacement.LastIndexOf('\n') + 1)..];
            }
            if (start < lower || start + length > upper) return;
            var span = source.Span(start, length);
            if (text.AsSpan(start, length).SequenceEqual(replacement)) return;
            edits.Add(new(Workspace.ToRange(span), replacement));
        }
        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.InterpolatedStringStart) interpolation++;
            bool protectedToken = directives.Contains(token.Line) || interpolation > 0;
            if (!protectedToken)
            {
                int start = previous == null ? 0 : previous.End + 1;
                // Identifier spans exclude their source escape. Whitespace ends
                // before that escape, so formatting can indent and space the
                // identifier without treating '@' as part of the gap.
                int tokenStart = token.Kind == TokenKind.Identifier && token.Start > 0 && text[token.Start - 1] == '@'
                    ? token.Start - 1 : token.Start;
                int length = tokenStart - start;
                if (length >= 0)
                {
                    string gap = text.Substring(start, length);
                    int depth = braces - (token.Kind == TokenKind.CloseBrace ? 1 : 0);
                    bool wasDirective = previous != null && directives.Contains(previous.Line);
                    bool hardBreak = previous?.Kind == TokenKind.OpenBrace || token.Kind == TokenKind.CloseBrace ||
                        previous?.Kind == TokenKind.Semicolon && parens == 0 ||
                        previous?.Kind == TokenKind.CloseBrace && token.Kind is not (TokenKind.Semicolon or TokenKind.Comma or TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.Dot) ||
                        token.Kind == TokenKind.OpenBrace && options.BracesOnNewLine;
                    bool separateUsing = previous?.Kind == TokenKind.Semicolon && usingLine && token.Kind != TokenKind.Using;
                    if (gap.All(char.IsWhiteSpace))
                    {
                        bool lineBreak = hardBreak || wasDirective || gap.Contains('\n') && !(token.Kind == TokenKind.OpenBrace && !options.BracesOnNewLine);
                        string replacement;
                        if (previous == null) replacement = Indent(depth);
                        else if (lineBreak)
                        {
                            int count = separateUsing || gap.Count(c => c == '\n') > 1 ? 2 : 1;
                            replacement = string.Concat(Enumerable.Repeat(newline, count)) + Indent(depth + (parens > 0 && token.Kind != TokenKind.CloseParen ? 1 : 0));
                        }
                        else
                        {
                            bool typeGap = InSameType(previous.Start, token.End);
                            replacement = unaryMinus.Contains(previous.Start)
                                ? token.Kind is TokenKind.Minus or TokenKind.MinusMinus ? " " : ""
                                : binaryOperators.Contains(previous.Start) || binaryOperators.Contains(token.Start) ? " "
                                : typeGap && previous.Kind != TokenKind.Comma ? gap : Spacing(previous, token, gap);
                        }
                        Edit(start, length, replacement);
                    }
                    else
                    {
                        // Preserve comment text and line structure, adjusting only
                        // the whitespace between its final newline and this token.
                        int lastLine = gap.LastIndexOf('\n');
                        if (lastLine >= 0 && gap.AsSpan(lastLine + 1).Trim().IsEmpty)
                            Edit(start + lastLine + 1, gap.Length - lastLine - 1, Indent(depth));
                    }
                }
                if (previous?.Kind == TokenKind.Semicolon) usingLine = false;
                if (token.Kind == TokenKind.Using) usingLine = true;
                if (token.Kind == TokenKind.OpenBrace) braces++;
                if (token.Kind == TokenKind.CloseBrace) braces = Math.Max(0, braces - 1);
                if (token.Kind == TokenKind.OpenParen) parens++;
                if (token.Kind == TokenKind.CloseParen) parens = Math.Max(0, parens - 1);
            }
            if (token.Kind == TokenKind.InterpolatedStringEnd) interpolation--;
            previous = token;
        }
        if (previous != null && !directives.Contains(previous.Line) && text.AsSpan(previous.End + 1).Trim().IsEmpty)
            Edit(previous.End + 1, text.Length - previous.End - 1, newline);
        var formatted = new System.Text.StringBuilder(text.Length);
        int cursor = 0;
        foreach (var edit in edits.OrderBy(e => Offset(e.Range.Start)))
        {
            int start = Offset(edit.Range.Start), end = Offset(edit.Range.End);
            formatted.Append(text, cursor, start - cursor).Append(edit.NewText);
            cursor = end;
        }
        formatted.Append(text, cursor, text.Length - cursor);
        var after = Lexer.Tokenize(new SourceFile(source.Path, formatted.ToString()), new diagnostics.DiagnosticBag()).Where(t => t.Kind != TokenKind.EndOfFile);
        if (!tokens.Select(t => (t.Kind, t.Text)).SequenceEqual(after.Select(t => (t.Kind, t.Text)))) return [];
        return edits.ToArray();
    }

    private static string Spacing(Token previous, Token current, string original)
    {
        if (previous.Kind == TokenKind.Identifier && current.Kind == TokenKind.StringLiteral && original.Length == 0) return "";
        if (current.Kind == TokenKind.OpenBracket && previous.Kind is TokenKind.Identifier or
            TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.Bang or TokenKind.StringLiteral or TokenKind.InterpolatedStringEnd) return "";
        if (current.Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.Comma or TokenKind.Semicolon or TokenKind.Dot or TokenKind.DoubleColon ||
            previous.Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.Dot or TokenKind.DoubleColon) return "";
        if (current.Kind == TokenKind.OpenParen) return previous.Kind is TokenKind.If or TokenKind.For or TokenKind.Foreach or TokenKind.While or TokenKind.Switch or TokenKind.Catch or TokenKind.Return or TokenKind.Throw ? " " : "";
        if (current.Kind is TokenKind.PlusPlus or TokenKind.MinusMinus || previous.Kind is TokenKind.Bang or TokenKind.Tilde) return "";
        if (current.Kind is TokenKind.Star or TokenKind.Ampersand or TokenKind.Caret or TokenKind.QuestionMark or TokenKind.Less or TokenKind.Greater or TokenKind.GreaterGreater ||
            previous.Kind is TokenKind.Star or TokenKind.Ampersand or TokenKind.Caret or TokenKind.QuestionMark or TokenKind.Less or TokenKind.Greater or TokenKind.GreaterGreater)
            return original;
        return " ";
    }
}
