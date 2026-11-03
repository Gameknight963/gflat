using System.Numerics;

namespace gflat.Preprocessing;

public sealed partial class Preprocessor
{
    private readonly record struct Integer(ulong Bits, bool Unsigned = false)
    {
        public long Signed => unchecked((long)Bits);
        public bool True => Bits != 0;
    }
    private static readonly Dictionary<string, int> Precedence = new()
    {
        ["||"] = 1, ["&&"] = 2, ["|"] = 3, ["^"] = 4, ["&"] = 5,
        ["=="] = 6, ["!="] = 6, ["<"] = 7, ["<="] = 7, [">"] = 7, [">="] = 7,
        ["<<"] = 8, [">>"] = 8, ["+"] = 9, ["-"] = 9, ["*"] = 10, ["/"] = 10, ["%"] = 10
    };

    private bool Evaluate(List<Piece> input, SourceSpan location)
    {
        var prepared = new List<Piece>();
        for (int i = 0; i < input.Count; i++)
        {
            if (input[i].Text != "defined") { prepared.Add(input[i]); continue; }
            var origin = input[i];
            bool parens = i + 1 < input.Count && input[i + 1].Text == "(";
            if (parens) i++;
            if (++i >= input.Count || !Name(input[i].Text)) throw Error("defined requires a macro name", origin.Span);
            Reference(input[i]);
            bool exists = macros.ContainsKey(input[i].Text) || input[i].Text is "__LINE__" or "__FILE__";
            if (parens && (++i >= input.Count || input[i].Text != ")")) throw Error("Expected ')' after defined", origin.Span);
            prepared.Add(origin with { Text = exists ? "1" : "0", Generated = true });
        }
        var tokens = Expand(prepared);
        int position = 0;
        if (tokens.Count == 0) throw Error("#if requires an expression", location);
        Integer Primary(bool enabled)
        {
            if (position == tokens.Count) throw Error("Incomplete #if expression", tokens[^1].Span);
            var token = tokens[position++];
            if (token.Text == "(")
            {
                var value = Expression(0, enabled);
                if (position == tokens.Count || tokens[position++].Text != ")") throw Error("Expected ')' in #if expression", token.Span);
                return value;
            }
            if (token.Text is "+" or "-" or "!" or "~")
            {
                var value = Primary(enabled);
                return token.Text switch
                {
                    "+" => value, "-" => new(unchecked(0UL - value.Bits), value.Unsigned),
                    "!" => new(value.True ? 0UL : 1UL), _ => new(~value.Bits, value.Unsigned)
                };
            }
            if (Name(token.Text)) return new(token.Text == "true" ? 1UL : 0UL);
            if (token.Text.StartsWith('\''))
            {
                try
                {
                    byte value = CharacterLiteralEncoding.Value(token.Text, token.Span.Line);
                    return new(unchecked((ulong)(long)(sbyte)value));
                }
                catch (gflat.CompileExceptions.TypeCheckException error) { throw Error(error.Description, token.Span); }
            }
            string digits = token.Text;
            int end = digits.Length;
            while (end > 0 && digits[end - 1] is 'u' or 'U' or 'l' or 'L') end--;
            string suffix = digits[end..].ToLowerInvariant();
            if (suffix is not ("" or "u" or "l" or "ul" or "lu" or "ll" or "ull" or "llu")) throw Error("Invalid integer suffix in #if", token.Span);
            digits = digits[..end];
            int radix = 10;
            if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { radix = 16; digits = digits[2..]; }
            else if (digits.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { radix = 2; digits = digits[2..]; }
            else if (digits.Length > 1 && digits[0] == '0') radix = 8;
            BigInteger number = 0;
            if (digits.Length == 0) throw Error("Invalid integer in #if", token.Span);
            foreach (char c in digits)
            {
                if (c == '_') continue;
                int digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                if (digit < 0 || digit >= radix) throw Error("#if requires an integer expression", token.Span);
                number = number * radix + digit;
            }
            if (number > ulong.MaxValue) throw Error("#if integer exceeds 64 bits", token.Span);
            return new((ulong)number, suffix.Contains('u') || number > long.MaxValue);
        }
        Integer Expression(int minimum, bool enabled)
        {
            Integer left = Primary(enabled);
            while (position < tokens.Count && Precedence.TryGetValue(tokens[position].Text, out int priority) && priority >= minimum)
            {
                var operation = tokens[position++];
                bool evaluateRight = enabled && (operation.Text != "&&" || left.True) && (operation.Text != "||" || !left.True);
                var right = Expression(priority + 1, evaluateRight);
                bool unsigned = left.Unsigned || right.Unsigned;
                bool compare = operation.Text is "==" or "!=" or "<" or ">" or "<=" or ">=" or "&&" or "||";
                if (!enabled) { left = new(0, !compare && unsigned); continue; }
                if (operation.Text is "/" or "%" && right.Bits == 0) throw Error("Division by zero in #if", operation.Span);
                if (operation.Text is "<<" or ">>" && right.Bits >= 64) throw Error("Invalid shift count in #if", operation.Span);
                if (!unsigned && operation.Text is "/" or "%" && left.Signed == long.MinValue && right.Signed == -1)
                    throw Error("Signed division overflow in #if", operation.Span);
                int relation = unsigned ? left.Bits.CompareTo(right.Bits) : left.Signed.CompareTo(right.Signed);
                ulong bits = operation.Text switch
                {
                    "+" => unchecked(left.Bits + right.Bits), "-" => unchecked(left.Bits - right.Bits), "*" => unchecked(left.Bits * right.Bits),
                    "/" => unsigned ? left.Bits / right.Bits : unchecked((ulong)(left.Signed / right.Signed)),
                    "%" => unsigned ? left.Bits % right.Bits : unchecked((ulong)(left.Signed % right.Signed)),
                    "<<" => left.Bits << (int)right.Bits, ">>" => left.Unsigned ? left.Bits >> (int)right.Bits : unchecked((ulong)(left.Signed >> (int)right.Bits)),
                    "&" => left.Bits & right.Bits, "|" => left.Bits | right.Bits, "^" => left.Bits ^ right.Bits,
                    "==" => relation == 0 ? 1UL : 0, "!=" => relation != 0 ? 1UL : 0,
                    "<" => relation < 0 ? 1UL : 0, "<=" => relation <= 0 ? 1UL : 0,
                    ">" => relation > 0 ? 1UL : 0, ">=" => relation >= 0 ? 1UL : 0,
                    "&&" => left.True && right.True ? 1UL : 0, _ => left.True || right.True ? 1UL : 0
                };
                left = new(bits, !compare && (operation.Text is "<<" or ">>" ? left.Unsigned : unsigned));
            }
            if (minimum == 0 && position < tokens.Count && tokens[position].Text == "?")
            {
                var question = tokens[position++];
                var yes = Expression(0, enabled && left.True);
                if (position == tokens.Count || tokens[position++].Text != ":") throw Error("Expected ':' in #if conditional", question.Span);
                var no = Expression(0, enabled && !left.True);
                left = new(left.True ? yes.Bits : no.Bits, yes.Unsigned || no.Unsigned);
            }
            return left;
        }
        var result = Expression(0, true);
        if (position != tokens.Count) throw Error("Unexpected token in #if expression", tokens[position].Span);
        return result.True;
    }
}
