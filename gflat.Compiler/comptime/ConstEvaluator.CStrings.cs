using System.Numerics;
using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private static BigInteger CStringIndex(ConstValue value, int line) => value switch
    {
        ConstValue.Integer integer => integer.Value,
        ConstValue.UInteger integer => integer.Value,
        ConstValue.WideInteger integer => integer.Value,
        ConstValue.Char character => unchecked((sbyte)character.Value),
        _ => throw new ConstEvalException("C-string pointer offsets must be integers", line)
    };

    private static ConstValue.Char ReadCString(ConstValue.String text, BigInteger index, int line)
    {
        byte[] bytes = StringLiteralEncoding.Bytes(text.Value, line);
        BigInteger absolute = text.Offset + index;
        if (absolute < 0 || absolute > bytes.Length)
            throw new ConstEvalException($"C-string index is outside its constant storage (length {bytes.Length + 1}, including terminator)", line);
        return new ConstValue.Char(absolute == bytes.Length ? '\0' : (char)bytes[(int)absolute]);
    }

    private void RequireCStringByteView(AstNode expression)
    {
        if (_context.ResolveAlias(_context.GetType(expression)) is not PointerTypeExpression pointer ||
            _context.ResolveAlias(pointer.Inner) is not NamedTypeExpression { Name: "char" })
            throw new ConstEvalException("Constant C-string access requires a char pointer; reinterpreted pointer views are not supported during constant evaluation", expression.Line);
    }

    private ConstValue.String OffsetCString(ConstValue.String text, ConstValue distance, AstNode pointer, bool subtract, int line)
    {
        RequireCStringByteView(pointer);
        BigInteger offset = text.Offset + (subtract ? -CStringIndex(distance, line) : CStringIndex(distance, line));
        int storageLength = StringLiteralEncoding.Bytes(text.Value, line).Length + 1;
        // An address one past the complete array is valid; reading it is not.
        if (offset < 0 || offset > storageLength)
            throw new ConstEvalException("C-string pointer offset is outside its constant storage", line);
        return text with { Offset = (int)offset };
    }

    private bool EvaluateCStringBinary(BinaryExpression expression, ConstValue left, ConstValue right)
    {
        if (left is not ConstValue.String && right is not ConstValue.String) return false;
        // Immutable string storage has an address even for an empty string.
        // Let ordinary pointer equality handle its comparison with null.
        if (expression.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals &&
            (left is ConstValue.Integer { Value: 0 } || right is ConstValue.Integer { Value: 0 }))
            return false;
        if (expression.Operator == TokenKind.Plus)
        {
            _currentValue = left is ConstValue.String text
                ? OffsetCString(text, right, expression.Left, false, expression.Line)
                : OffsetCString((ConstValue.String)right, left, expression.Right, false, expression.Line);
            return true;
        }
        if (expression.Operator == TokenKind.Minus && left is ConstValue.String origin && right is not ConstValue.String)
        {
            _currentValue = OffsetCString(origin, right, expression.Left, true, expression.Line);
            return true;
        }
        if (left is ConstValue.String a && right is ConstValue.String b)
        {
            bool sameStorage = StringLiteralEncoding.Bytes(a.Value, expression.Line).AsSpan()
                .SequenceEqual(StringLiteralEncoding.Bytes(b.Value, expression.Line));
            if (expression.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals)
            {
                // Different arrays cannot overlap, but a one-past address can
                // coincide with a neighboring array's start. Its placement is
                // not available during constant evaluation.
                if (!sameStorage && (a.Offset > StringLiteralEncoding.Bytes(a.Value).Length || b.Offset > StringLiteralEncoding.Bytes(b.Value).Length))
                    throw new ConstEvalException("Cannot compare one-past addresses of distinct constant strings", expression.Line);
                bool equal = sameStorage && a.Offset == b.Offset;
                _currentValue = new ConstValue.Boolean(expression.Operator == TokenKind.EqualsEquals ? equal : !equal);
                return true;
            }
            if (!sameStorage)
                throw new ConstEvalException("Cannot order or subtract addresses of distinct constant strings", expression.Line);
            RequireCStringByteView(expression.Left);
            RequireCStringByteView(expression.Right);
            _currentValue = expression.Operator switch
            {
                TokenKind.Minus => new ConstValue.Integer(a.Offset - b.Offset),
                TokenKind.Less => new ConstValue.Boolean(a.Offset < b.Offset),
                TokenKind.LessEquals => new ConstValue.Boolean(a.Offset <= b.Offset),
                TokenKind.Greater => new ConstValue.Boolean(a.Offset > b.Offset),
                TokenKind.GreaterEquals => new ConstValue.Boolean(a.Offset >= b.Offset),
                _ => throw new ConstEvalException("Unsupported constant C-string pointer operation", expression.Line)
            };
            return true;
        }
        throw new ConstEvalException("Unsupported constant C-string pointer operation", expression.Line);
    }

    private bool StepCString(ConstValue value, AstNode target, bool subtract, out ConstValue? result)
    {
        if (value is ConstValue.String text)
        {
            result = OffsetCString(text, new ConstValue.Integer(1), target, subtract, target.Line);
            return true;
        }
        result = null;
        return false;
    }
}
