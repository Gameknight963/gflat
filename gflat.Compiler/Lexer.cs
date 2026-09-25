using gflat.CompileExceptions;

namespace gflat;

public class Lexer
{
    public static List<Token> Tokenize(SourceFile source)
    {
        using var context = SourceContext.Enter(source.Span(0));
        var tokens = Tokenize(source.Text);
        foreach (var token in tokens) token.Source = source;
        return tokens;
    }

    public static List<Token> Tokenize(string code)
    {
        List<Token> tokens = new();
        int line = 1;
        for (int i = 0; i < code.Length;)
        {
            using var context = SourceContext.Enter(SourceContext.Current?.Source is { } source && source.Text == code ? source.Span(i, 1) : default);
            char c = code[i];

            if (char.IsWhiteSpace(c))
            {
                if (c == '\n') line++;
                i++;
                continue;
            }

            if (c == '/' && i + 1 < code.Length && code[i + 1] == '/')
            {
                while (i < code.Length && code[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < code.Length && code[i + 1] == '*')
            {
                i += 2;
                int commentLine = line;
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/'))
                {
                    if (code[i] == '\n') line++;
                    i++;
                }
                if (i + 1 >= code.Length) throw new TypeCheckException("Unterminated block comment", commentLine);
                i += 2;
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < code.Length && (char.IsAsciiLetterOrDigit(code[i]) || code[i] == '_'))
                    i++;
                string text = code[start..i];
                TokenKind kind = GetKeyword(text);
                tokens.Add(new Token(kind, line, start, i - 1, kind == TokenKind.Identifier ? text : null));
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                Token number = ReadNumber(code, i, out int end, line);
                string spelling = number.Text;
                for (int n = 0; n < spelling.Length; n++)
                    if (spelling[n] == '_' && (n == 0 || n + 1 == spelling.Length ||
                        !char.IsAsciiDigit(spelling[n - 1]) || !char.IsAsciiDigit(spelling[n + 1])))
                        throw new TypeCheckException("Numeric separators must occur between digits", line);
                if (spelling == "0x" || spelling == "0X") throw new TypeCheckException("Expected hexadecimal digits", line);
                if (spelling.Contains('.') && number.Kind is TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral)
                    throw new TypeCheckException("Integer suffix on floating-point literal", line);
                tokens.Add(new Token(number.Kind, line, number.Start, number.End, spelling.Replace("_", "")));
                i = end;
                continue;
            }

            switch (c)
            {
                case '=':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.EqualsEquals, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '>')
                    { tokens.Add(new Token(TokenKind.EqualsGreater, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Equals, line, i, i)); break;
                case '<':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.LessEquals, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '<')
                    { tokens.Add(new Token(TokenKind.LessLess, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Less, line, i, i)); break;
                case '>':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.GreaterEquals, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '>')
                    { tokens.Add(new Token(TokenKind.GreaterGreater, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Greater, line, i, i)); break;
                case '|':
                    if (i + 1 < code.Length && code[i + 1] == '|')
                    { tokens.Add(new Token(TokenKind.PipePipe, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Pipe, line, i, i)); break;
                case '&':
                    if (i + 1 < code.Length && code[i + 1] == '&')
                    { tokens.Add(new Token(TokenKind.AmpersandAmpersand, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Ampersand, line, i, i)); break;
                case '!':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.NotEquals, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Bang, line, i, i)); break;
                case '+':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.PlusEquals, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '+')
                    { tokens.Add(new Token(TokenKind.PlusPlus, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Plus, line, i, i)); break;
                case '-':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.MinusEquals, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '-')
                    { tokens.Add(new Token(TokenKind.MinusMinus, line, i, i + 1)); i++; break; }
                    if (i + 1 < code.Length && code[i + 1] == '>')
                    { tokens.Add(new Token(TokenKind.Arrow, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Minus, line, i, i)); break;
                case '*':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.StarEquals, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Star, line, i, i)); break;
                case '/':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.SlashEquals, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Slash, line, i, i)); break;
                case '%':
                    if (i + 1 < code.Length && code[i + 1] == '=')
                    { tokens.Add(new Token(TokenKind.PercentEquals, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Percent, line, i, i)); break;
                case ':':
                    if (i + 1 < code.Length && code[i + 1] == ':')
                    { tokens.Add(new Token(TokenKind.DoubleColon, line, i, i + 1)); i++; break; }
                    tokens.Add(new Token(TokenKind.Colon, line, i, i)); break;
                case '\'':
                    {
                        int charStart = i;
                        i++; // skip opening '
                        if (i >= code.Length) throw new TypeCheckException($"Unterminated char literal on line {line}", line);
                        if (code[i] == '\\')
                        {
                            i++; // skip backslash
                            if (i >= code.Length) throw new TypeCheckException($"Unterminated char literal on line {line}", line);
                            i++; // skip escaped char
                        }
                        else
                        {
                            i++; // skip regular char
                        }
                        if (i >= code.Length || code[i] != '\'')
                            throw new TypeCheckException($"Invalid char literal on line {line}", line);
                        tokens.Add(new Token(TokenKind.CharLiteral, line, charStart, i, code[charStart..(i + 1)]));
                        break;
                    }
                case '"':
                    tokens.Add(ReadString(code, i, out int strEnd, line));
                    i = strEnd;
                    continue;
                case '$':
                    if (i + 1 < code.Length && code[i + 1] == '"')
                    {
                        tokens.AddRange(ReadInterpolatedString(code, i, out int istrEnd, line));
                        for (int offset = i; offset < istrEnd; offset++) if (code[offset] == '\n') line++;
                        i = istrEnd;
                        continue;
                    }
                    tokens.Add(new Token(TokenKind.Dollar, line, i, i));
                    break;

                case '^': tokens.Add(new Token(TokenKind.Caret, line, i, i)); break;
                case '?': tokens.Add(new Token(TokenKind.QuestionMark, line, i, i)); break;
                case ',': tokens.Add(new Token(TokenKind.Comma, line, i, i)); break;
                case '.': tokens.Add(new Token(TokenKind.Dot, line, i, i)); break;
                case ';': tokens.Add(new Token(TokenKind.Semicolon, line, i, i)); break;
                case '(': tokens.Add(new Token(TokenKind.OpenParen, line, i, i)); break;
                case ')': tokens.Add(new Token(TokenKind.CloseParen, line, i, i)); break;
                case '{': tokens.Add(new Token(TokenKind.OpenBrace, line, i, i)); break;
                case '}': tokens.Add(new Token(TokenKind.CloseBrace, line, i, i)); break;
                case '~': tokens.Add(new Token(TokenKind.Tilde, line, i, i)); break;
                case '[': tokens.Add(new Token(TokenKind.OpenBracket, line, i, i)); break;
                case ']': tokens.Add(new Token(TokenKind.CloseBracket, line, i, i)); break;
                case '#': tokens.Add(new Token(TokenKind.Hash, line, i, i)); break;
                default:
                    throw new TypeCheckException($"Unexpected character '{c}' on line {line}", line);
            }
            i++;
        }
        tokens.Add(new Token(TokenKind.EndOfFile, line, code.Length, code.Length));
        var lineStarts = new List<int> { 0 };
        for (int offset = 0; offset < code.Length; offset++) if (code[offset] == '\n') lineStarts.Add(offset + 1);
        for (int t = 0; t < tokens.Count; t++)
        {
            Token token = tokens[t];
            int row = lineStarts.BinarySearch(token.Start);
            if (row < 0) row = ~row - 1;
            tokens[t] = new Token(token.Kind, row + 1, token.Start, token.End, token.Text, token.Start - lineStarts[row] + 1);
        }
        return tokens;
    }

    private static TokenKind GetKeyword(string text)
    {
        return text switch
        {
            "namespace" => TokenKind.Namespace,
            "using" => TokenKind.Using,
            "class" => TokenKind.Class,
            "struct" => TokenKind.Struct,
            "interface" => TokenKind.Interface,
            "enum" => TokenKind.Enum,
            "public" => TokenKind.Public,
            "private" => TokenKind.Private,
            "protected" => TokenKind.Protected,
            "internal" => TokenKind.Internal,
            "new" => TokenKind.New,
            "delete" => TokenKind.Delete,
            "return" => TokenKind.Return,
            "if" => TokenKind.If,
            "else" => TokenKind.Else,
            "while" => TokenKind.While,
            "for" => TokenKind.For,
            "foreach" => TokenKind.Foreach,
            "in" => TokenKind.In,
            "repeat" => TokenKind.Repeat,
            "break" => TokenKind.Break,
            "continue" => TokenKind.Continue,
            "switch" => TokenKind.Switch,
            "case" => TokenKind.Case,
            "default" => TokenKind.Default,
            "unsafe" => TokenKind.Unsafe,
            "const" => TokenKind.Const,
            "static" => TokenKind.Static,
            "readonly" => TokenKind.Readonly,
            "virtual" => TokenKind.Virtual,
            "override" => TokenKind.Override,
            "weak" => TokenKind.Weak,
            "replace" => TokenKind.Replace,
            "abstract" => TokenKind.Abstract,
            "null" => TokenKind.Null,
            "true" => TokenKind.True,
            "false" => TokenKind.False,
            "void" => TokenKind.Void,
            "int" => TokenKind.Int,
            "float" => TokenKind.Float,
            "bool" => TokenKind.Bool,
            "char" => TokenKind.Char,
            "long" => TokenKind.Long,
            "extralong" => TokenKind.ExtraLong,
            "uint" => TokenKind.UInt,
            "ulong" => TokenKind.ULong,
            "nint" => TokenKind.NInt,
            "nuint" => TokenKind.NUInt,
            "byte" => TokenKind.Byte,
            "sbyte" => TokenKind.SByte,
            "short" => TokenKind.Short,
            "ushort" => TokenKind.UShort,
            "string" => TokenKind.String,
            "extern" => TokenKind.Extern,
            "defer" => TokenKind.Defer,
            "alias" => TokenKind.Alias,
            "global" => TokenKind.Global,
            "operator" => TokenKind.Operator,
            "base" => TokenKind.Base,
            "sizeof" => TokenKind.Sizeof,
            "nameof" => TokenKind.Nameof,
            "throw" => TokenKind.Throw,
            "throws" => TokenKind.Throws,
            "try" => TokenKind.Try,
            "catch" => TokenKind.Catch,
            _ => TokenKind.Identifier
        };
    }

    private static Token ReadString(string source, int startIndex, out int endIndex, int line)
    {
        int i = startIndex + 1;
        while (true)
        {
            if (i >= source.Length) throw new TypeCheckException($"Unterminated string literal on line {line}", line);
            if (source[i] is '\n' or '\r') throw new TypeCheckException("Newline in string literal; use an escape", line);
            if (source[i] == '\\') { i += 2; continue; } // skip escape sequences
            if (source[i] == '"') { endIndex = i + 1; return new Token(TokenKind.StringLiteral, line, startIndex, i, source[startIndex..(i + 1)]); }
            i++;
        }
    }

    private static List<Token> ReadInterpolatedString(string source, int startIndex, out int endIndex, int line, bool tokenizeExpressions = true)
    {
        var tokens = new List<Token> { new(TokenKind.InterpolatedStringStart, line, startIndex, startIndex + 1) };
        int i = startIndex + 2, segmentStart = i;
        var segment = new System.Text.StringBuilder();
        void Flush()
        {
            tokens.Add(new(TokenKind.InterpolatedStringSegment, line, segmentStart, Math.Max(segmentStart, i - 1), segment.ToString()));
            segment.Clear();
        }
        while (i < source.Length)
        {
            char c = source[i];
            if (c is '\n' or '\r') throw new TypeCheckException("Newline in interpolated string; use an escape", line);
            if (c == '\\')
            {
                if (i + 1 >= source.Length) break;
                segment.Append(source[i++]); segment.Append(source[i++]); continue;
            }
            if ((c == '{' || c == '}') && i + 1 < source.Length && source[i + 1] == c)
            { segment.Append(c); i += 2; continue; }
            if (c == '}') throw new TypeCheckException("Unmatched '}' in interpolated string; use '}}' for literal text", line);
            if (c == '"')
            {
                Flush();
                tokens.Add(new(TokenKind.InterpolatedStringEnd, line, i, i));
                endIndex = i + 1;
                return tokens;
            }
            if (c != '{') { segment.Append(c); i++; continue; }
            Flush();
            tokens.Add(new(TokenKind.InterpolatedStringExprStart, line, i, i));
            int expressionStart = ++i, depth = 1;
            while (i < source.Length && depth > 0)
            {
                if (source[i] == '$' && i + 1 < source.Length && source[i + 1] == '"')
                { ReadInterpolatedString(source, i, out i, line, tokenizeExpressions: false); continue; }
                if (source[i] == '"') { ReadString(source, i, out i, line); continue; }
                if (source[i] == '\'')
                {
                    i++;
                    while (i < source.Length && source[i] != '\'')
                    { if (source[i] == '\\') i++; i++; }
                    if (i >= source.Length) break;
                    i++; continue;
                }
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '/')
                { while (i < source.Length && source[i] != '\n') i++; continue; }
                if (source[i] == '/' && i + 1 < source.Length && source[i + 1] == '*')
                {
                    i += 2;
                    while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/')) i++;
                    if (i + 1 >= source.Length) break;
                    i += 2; continue;
                }
                if (source[i] == '{') depth++;
                if (source[i] == '}') depth--;
                if (depth == 0) break;
                i++;
            }
            if (i >= source.Length || depth != 0) throw new TypeCheckException("Unterminated interpolation expression", line);
            // The delimiter scan must not tokenize nested expressions: doing so
            // here and again when tokenizing the hole grows exponentially with nesting.
            if (tokenizeExpressions)
            {
                var expressionTokens = Tokenize(source[expressionStart..i]);
                tokens.AddRange(expressionTokens.Take(expressionTokens.Count - 1).Select(t =>
                    new Token(t.Kind, line + t.Line - 1, expressionStart + t.Start, expressionStart + t.End, t.Text)));
            }
            tokens.Add(new(TokenKind.InterpolatedStringExprEnd, line, i, i));
            segmentStart = ++i;
        }
        throw new TypeCheckException("Unterminated interpolated string", line);
    }
    private static Token ReadNumber(string source, int startIndex, out int endIndex, int line)
    {
        int i = startIndex;

        if (i + 1 < source.Length && source[i] == '0' && source[i + 1] == 'x')
        {
            i += 2;
            while (i < source.Length && char.IsAsciiHexDigit(source[i])) i++;
            if (i == startIndex + 2) throw new TypeCheckException("Expected hexadecimal digits", line);
            TokenKind hexKind = TokenKind.HexInt;
            if (i < source.Length && char.ToLower(source[i]) == 'u')
            {
                i++;
                if (i < source.Length && char.ToLower(source[i]) == 'l') { i++; hexKind = TokenKind.ULongLiteral; }
                else hexKind = TokenKind.UIntLiteral;
            }
            else if (i < source.Length && char.ToLower(source[i]) == 'l')
            {
                i++;
                if (i < source.Length && char.ToLower(source[i]) == 'u') { i++; hexKind = TokenKind.ULongLiteral; }
                else hexKind = TokenKind.LongLiteral;
            }
            endIndex = i;
            return new Token(hexKind, line, startIndex, i - 1, source[startIndex..i]);
        }

        while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '_')) i++;

        bool hasDecimalPoint = false;
        if (i < source.Length && source[i] == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1]))
        {
            i++;
            hasDecimalPoint = true;
            while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '_')) i++;
        }

        TokenKind kind = TokenKind.IntLiteral;
        if (i < source.Length)
        {
            char ch = char.ToLower(source[i]);
            if (ch == 'u')
            {
                i++;
                if (i < source.Length && char.ToLower(source[i]) == 'l')
                {
                    i++;
                    kind = TokenKind.ULongLiteral;
                }
                else
                {
                    kind = TokenKind.UIntLiteral;
                }
            }
            else if (ch == 'l')
            {
                i++;
                if (i < source.Length && char.ToLower(source[i]) == 'u')
                {
                    i++;
                    kind = TokenKind.ULongLiteral;
                }
                else
                {
                    kind = TokenKind.LongLiteral;
                }
            }
            else
            {
                switch (ch)
                {
                    case 'f': kind = TokenKind.FloatLiteral; i++; break;
                    case 'd': kind = TokenKind.DoubleLiteral; i++; break;
                    case 'm': kind = TokenKind.DecimalLiteral; i++; break;
                }
            }
        }
        if (hasDecimalPoint && kind == TokenKind.IntLiteral) kind = TokenKind.DoubleLiteral;

        endIndex = i;
        return new Token(kind, line, startIndex, i - 1, source[startIndex..i]);
    }
}
