using System.Numerics;

namespace gflat.comptime;

public abstract partial record ConstValue
{
    public sealed record WideInteger(BigInteger Value) : ConstValue
    {
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static bool TryIntegerValue(ConstValue value, out BigInteger integer)
    {
        switch (value)
        {
            case Integer signed: integer = signed.Value; return true;
            case UInteger unsigned: integer = unsigned.Value; return true;
            case WideInteger wide: integer = wide.Value; return true;
            case Char character: integer = unchecked((sbyte)character.Value); return true;
            default: integer = default; return false;
        }
    }

    private static bool TryWideOperands(ConstValue left, ConstValue right, out BigInteger a, out BigInteger b)
    {
        a = b = default;
        return (left is WideInteger || right is WideInteger) &&
            TryIntegerValue(left, out a) && TryIntegerValue(right, out b);
    }

    private static bool TryWideArithmetic(TokenKind operation, ConstValue left, ConstValue right, out ConstValue? result)
    {
        result = null;
        if (!TryWideOperands(left, right, out var a, out var b)) return false;
        // Width normalization belongs to the typed evaluator, just as for the
        // existing integer variants. Intermediates retain all bits until then.
        result = new WideInteger(operation switch
        {
            TokenKind.Plus => a + b,
            TokenKind.Minus => a - b,
            TokenKind.Star => a * b,
            TokenKind.Ampersand => a & b,
            TokenKind.Pipe => a | b,
            TokenKind.Caret => a ^ b,
            _ => throw new InvalidOperationException("Unsupported wide integer operation")
        });
        return true;
    }
}
