using System.Globalization;
using System.Numerics;
using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<BinaryExpression, TypeExpression> _nativeIntegerOperands = new();

    public bool TryGetNativeIntegerOperands(BinaryExpression expression, out TypeExpression type)
        => _nativeIntegerOperands.TryGetValue(expression, out type!);

    // A representable literal can adopt the other operand's native integer type,
    // just as it can when passed to a native integer parameter or initializer.
    // Variables retain their declared signedness; this is not an implicit cast
    // between arbitrary signed and unsigned values. Shift counts are independent.
    private void ContextualizeNativeIntegerOperands(BinaryExpression expression,
        ref TypeExpression left, ref TypeExpression right)
    {
        if (expression.Operator is TokenKind.LessLess or TokenKind.GreaterGreater ||
            !IsInteger(left) || !IsInteger(right)) return;

        TypeExpression? common = null;
        if (ResolveAlias(left) is NamedTypeExpression { Name: "nint" or "nuint" } nativeLeft &&
            TryGetIntegerLiteralValue(expression.Right, out BigInteger rightValue) &&
            FitsIntegerLiteral(nativeLeft.Name, rightValue))
            common = nativeLeft;
        else if (ResolveAlias(right) is NamedTypeExpression { Name: "nint" or "nuint" } nativeRight &&
            TryGetIntegerLiteralValue(expression.Left, out BigInteger leftValue) &&
            FitsIntegerLiteral(nativeRight.Name, leftValue))
            common = nativeRight;

        if (common == null) return;
        _nativeIntegerOperands[expression] = common;
        left = right = common;
    }

    internal static bool TryGetIntegerLiteralValue(AstNode expression, out BigInteger value)
    {
        if (expression is UnaryExpression { IsPrefix: true, Operator: TokenKind.Plus or TokenKind.Minus } unary &&
            TryGetIntegerLiteralValue(unary.Operand, out value))
        {
            if (unary.Operator == TokenKind.Minus) value = -value;
            return true;
        }
        if (expression is LiteralExpression literal && literal.Token.Kind is TokenKind.IntLiteral or
            TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral or TokenKind.HexInt)
        {
            string text = literal.Token.Text.TrimEnd('u', 'U', 'l', 'L');
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                // Leading zero makes the hexadecimal BigInteger explicitly positive.
                return BigInteger.TryParse("0" + text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
            return BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }
        value = default;
        return false;
    }

    private static bool FitsIntegerLiteral(string name, BigInteger value)
    {
        int bits = name switch
        {
            "byte" or "sbyte" => 8,
            "short" or "ushort" => 16,
            "int" or "uint" => 32,
            "long" or "ulong" => 64,
            "nint" or "nuint" => TargetInfo.Default.PointerBits,
            "extralong" => 128,
            _ => 0
        };
        if (bits == 0) return false;
        bool unsigned = name is "byte" or "ushort" or "uint" or "ulong" or "nuint";
        return unsigned ? value >= 0 && value < (BigInteger.One << bits) :
            value >= -(BigInteger.One << (bits - 1)) && value < (BigInteger.One << (bits - 1));
    }
}
