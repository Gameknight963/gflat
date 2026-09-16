namespace gflat;

public class Lexer
{
    public static List<Token> Tokenize(string code)
    {
        List<Token> tokens = new();
        int line = 1;
        for (int i = 0; i < code.Length;)
        {
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
                while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) i++;
                i += 2;
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < code.Length && (char.IsLetterOrDigit(code[i]) || code[i] == '_'))
                    i++;
                string text = code[start..i];
                TokenKind kind = GetKeyword(text);
                tokens.Add(new Token(kind, line, start, i - 1, kind == TokenKind.Identifier ? text : null));
                continue;
            }

            if (char.IsAsciiDigit(c))
            {
                tokens.Add(ReadNumber(code, i, out int end, line));
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
                        if (i >= code.Length) throw new Exception($"Unterminated char literal on line {line}");
                        if (code[i] == '\\')
                        {
                            i++; // skip backslash
                            if (i >= code.Length) throw new Exception($"Unterminated char literal on line {line}");
                            i++; // skip escaped char
                        }
                        else
                        {
                            i++; // skip regular char
                        }
                        if (i >= code.Length || code[i] != '\'')
                            throw new Exception($"Invalid char literal on line {line}");
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
                default:
                    throw new Exception($"Unexpected character '{c}' on line {line}");
            }
            i++;
        }
        tokens.Add(new Token(TokenKind.EndOfFile, line, code.Length, code.Length));
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
            if (i >= source.Length) throw new Exception($"Unterminated string literal on line {line}");
            if (source[i] == '\\') { i += 2; continue; } // skip escape sequences
            if (source[i] == '"') { endIndex = i + 1; return new Token(TokenKind.StringLiteral, line, startIndex, i, source[startIndex..(i + 1)]); }
            i++;
        }
    }

    private static List<Token> ReadInterpolatedString(string source, int startIndex, out int endIndex, int line)
    {
        List<Token> tokens = new();
        int i = startIndex + 2; // skip $"
        int segStart = i;

        while (true)
        {
            if (i >= source.Length) throw new Exception($"Unterminated interpolated string on line {line}");
            if (source[i] == '\\') { i += 2; continue; }

            if (source[i] == '{')
            {
                // emit text segment before the expression
                tokens.Add(new Token(TokenKind.InterpolatedStringSegment, line, segStart, i - 1, source[segStart..i]));
                tokens.Add(new Token(TokenKind.InterpolatedStringExprStart, line, i, i));
                i++; // skip {

                // tokenize the expression inside {}
                int depth = 1;
                int exprStart = i;
                while (i < source.Length && depth > 0)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}') depth--;
                    if (depth > 0) i++;
                }

                string expr = source[exprStart..i];
                List<Token> exprTokens = Tokenize(expr);
                exprTokens.RemoveAt(exprTokens.Count - 1); // remove EOF
                tokens.AddRange(exprTokens);

                tokens.Add(new Token(TokenKind.InterpolatedStringExprEnd, line, i, i));
                i++; // skip }
                segStart = i;
                continue;
            }

            if (source[i] == '"')
            {
                tokens.Add(new Token(TokenKind.InterpolatedStringSegment, line, segStart, i - 1, source[segStart..i]));
                endIndex = i + 1;
                return tokens;
            }

            i++;
        }
    }
    private static Token ReadNumber(string source, int startIndex, out int endIndex, int line)
    {
        int i = startIndex;

        if (i + 1 < source.Length && source[i] == '0' && source[i + 1] == 'x')
        {
            i += 2;
            while (i < source.Length && char.IsAsciiHexDigit(source[i])) i++;
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