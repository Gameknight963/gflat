using System;
using System.Collections.Generic;
using System.Text;

namespace gflat
{
    public class Token
    {
        public TokenKind Kind { get; }
        public string Text { get; }
        public int Line { get; }
        public int Start { get; }
        public int End { get; }

        public Token(TokenKind kind, int line, int start, int end, string? text = null)
        {
            Kind = kind;
            Line = line;
            Start = start;
            End = end;
            Text = text ?? DefaultText(kind);
        }

        public override string ToString() => $"{Kind,-30} {Text}";

        private static string DefaultText(TokenKind kind) => kind switch
        {
            TokenKind.Plus => "+",
            TokenKind.Minus => "-",
            TokenKind.Star => "*",
            TokenKind.Slash => "/",
            TokenKind.Equals => "=",
            TokenKind.EqualsEquals => "==",
            TokenKind.NotEquals => "!=",
            TokenKind.Less => "<",
            TokenKind.Greater => ">",
            TokenKind.LessEquals => "<=",
            TokenKind.GreaterEquals => ">=",
            TokenKind.LessLess => "<<",
            TokenKind.GreaterGreater => ">>",
            TokenKind.PlusEquals => "+=",
            TokenKind.MinusEquals => "-=",
            TokenKind.StarEquals => "*=",
            TokenKind.SlashEquals => "/=",
            TokenKind.PercentEquals => "%=",
            TokenKind.PlusPlus => "++",
            TokenKind.MinusMinus => "--",
            TokenKind.AmpersandAmpersand => "&&",
            TokenKind.PipePipe => "||",
            TokenKind.Bang => "!",
            TokenKind.Ampersand => "&",
            TokenKind.Pipe => "|",
            TokenKind.Dot => ".",
            TokenKind.Arrow => "->",
            TokenKind.DoubleColon => "::",
            TokenKind.Colon => ":",
            TokenKind.Semicolon => ";",
            TokenKind.Comma => ",",
            TokenKind.OpenParen => "(",
            TokenKind.CloseParen => ")",
            TokenKind.OpenBrace => "{",
            TokenKind.CloseBrace => "}",
            TokenKind.OpenBracket => "[",
            TokenKind.CloseBracket => "]",
            TokenKind.QuestionMark => "?",
            TokenKind.Tilde => "~",
            TokenKind.Caret => "^",
            TokenKind.Dollar => "$",
            TokenKind.EqualsGreater => "=>",
            TokenKind.Percent => "%",
            TokenKind.EndOfFile => "",
            TokenKind.Namespace => "namespace",
            TokenKind.Using => "using",
            TokenKind.Class => "class",
            TokenKind.Struct => "struct",
            TokenKind.Interface => "interface",
            TokenKind.Enum => "enum",
            TokenKind.Public => "public",
            TokenKind.Private => "private",
            TokenKind.Protected => "protected",
            TokenKind.Internal => "internal",
            TokenKind.New => "new",
            TokenKind.Return => "return",
            TokenKind.If => "if",
            TokenKind.Else => "else",
            TokenKind.While => "while",
            TokenKind.For => "for",
            TokenKind.Foreach => "foreach",
            TokenKind.Repeat => "repeat",
            TokenKind.Break => "break",
            TokenKind.Continue => "continue",
            TokenKind.Switch => "switch",
            TokenKind.Case => "case",
            TokenKind.Default => "default",
            TokenKind.Unsafe => "unsafe",
            TokenKind.Const => "const",
            TokenKind.Static => "static",
            TokenKind.Readonly => "readonly",
            TokenKind.Null => "null",
            TokenKind.Extern => "extern",
            TokenKind.Defer => "defer",
            TokenKind.Alias => "alias",
            TokenKind.True => "true",
            TokenKind.False => "false",
            TokenKind.Void => "void",
            TokenKind.Int => "int",
            TokenKind.Float => "float",
            TokenKind.Bool => "bool",
            TokenKind.Char => "char",
            TokenKind.Long => "long",
            TokenKind.ExtraLong => "extralong",
            TokenKind.UInt => "uint",
            TokenKind.ULong => "ulong",
            TokenKind.NInt => "nint",
            TokenKind.NUInt => "nuint",
            TokenKind.Byte => "byte",
            TokenKind.SByte => "sbyte",
            TokenKind.Short => "short",
            TokenKind.UShort => "ushort",
            TokenKind.UIntLiteral => "0u",
            TokenKind.ULongLiteral => "0ul",
            TokenKind.String => "string",
            TokenKind.Global => "global",
            TokenKind.Operator => "operator",
            TokenKind.Virtual => "virtual",
            TokenKind.Override => "override",
            TokenKind.Abstract => "abstract",
            TokenKind.Base => "base",
            TokenKind.Sizeof => "sizeof",
            TokenKind.Nameof => "nameof",
            TokenKind.InterpolatedStringExprStart => "{",
            TokenKind.InterpolatedStringExprEnd => "}",
            TokenKind.Throw => "throw",
            TokenKind.Try => "try",
            TokenKind.Catch => "catch",
            _ => throw new InvalidOperationException($"Cannot infer token text of '{kind}'")
        };
    }
}
