using System.Text;

namespace gflat.Preprocessing;

public sealed partial class Preprocessor
{
    private static readonly string[] Punctuation = ["%:%:", ">>=", "<<=", "...", "##", "::", "=>", "==", "!=", "<=", ">=", "<<", ">>", "++", "--", "&&", "||", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "??", "?.", "[:", ":]", "%:"];

    private List<Piece> Scan(SourceFile source)
    {
        var buffer = new StringBuilder();
        var offsets = new List<int>();
        for (int n = 0; n < source.Text.Length; n++)
        {
            if (source.Text[n] == '\\' && n + 1 < source.Text.Length && source.Text[n + 1] is '\r' or '\n')
            {
                n++;
                if (source.Text[n] == '\r' && n + 1 < source.Text.Length && source.Text[n + 1] == '\n') n++;
                continue;
            }
            buffer.Append(source.Text[n]); offsets.Add(n);
        }
        string text = buffer.ToString();
        var result = new List<Piece>();
        bool space = false;
        for (int i = 0; i < text.Length;)
        {
            cancellation.ThrowIfCancellationRequested();
            int start = i;
            char c = text[i];
            if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                i++; Add("\n"); space = true; continue;
            }
            if (char.IsWhiteSpace(c)) { space = true; i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] is not ('\r' or '\n')) i++;
                space = true; continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                if (i + 1 >= text.Length) throw Error("Unterminated block comment", source.Span(offsets[start]));
                i += 2; space = true; continue;
            }
            if (c == '$' && i + 1 < text.Length && text[i + 1] == '"')
            {
                try { Lexer.ReadInterpolatedString(text, i, out i, source.Span(offsets[start]).Line, tokenizeExpressions: false); }
                catch (gflat.CompileExceptions.TypeCheckException error) { throw Error(error.Description, source.Span(offsets[start])); }
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
            }
            else if (char.IsAsciiDigit(c) || c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1]))
            {
                i++;
                while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '.' ||
                    text[i] is '+' or '-' && text[i - 1] is 'e' or 'E' or 'p' or 'P')) i++;
            }
            else if (c is '"' or '\'')
            {
                i++;
                while (i < text.Length && text[i] != c)
                {
                    if (text[i] == '\\' && i + 1 < text.Length) i++;
                    i++;
                }
                if (i == text.Length) throw Error("Unterminated literal", source.Span(offsets[start]));
                i++;
            }
            else
            {
                string? punctuation = Punctuation.FirstOrDefault(p => text.AsSpan(i).StartsWith(p, StringComparison.Ordinal));
                i += punctuation?.Length ?? 1;
            }
            Add(text[start..i]);

            void Add(string spelling)
            {
                result.Add(new(spelling, source.Span(offsets[start], offsets[i - 1] - offsets[start] + 1), space));
                space = false;
            }
        }
        return result;
    }
}
