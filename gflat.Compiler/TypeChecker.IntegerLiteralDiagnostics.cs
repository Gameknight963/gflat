using System.Numerics;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<LiteralExpression> signedMinimumOperands = new();

    internal static void ValidateIntegerLiteralSuffix(LiteralExpression literal, bool allowSignedMinimum = false)
    {
        if (literal.Token.Kind is not (TokenKind.UIntLiteral or TokenKind.LongLiteral or TokenKind.ULongLiteral)) return;
        if (!TryGetIntegerLiteralValue(literal, out BigInteger value))
            throw new TypeCheckException("Invalid integer literal", literal.Span);
        bool hexadecimal = literal.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        BigInteger maximum = literal.Token.Kind switch {
            TokenKind.UIntLiteral => uint.MaxValue,
            TokenKind.LongLiteral when !hexadecimal => long.MaxValue,
            _ => ulong.MaxValue
        };
        if (value <= maximum || literal.Token.Kind == TokenKind.LongLiteral &&
            !hexadecimal && value == BigInteger.One << 63 && allowSignedMinimum) return;
        throw new TypeCheckException("Integer literal is outside the range allowed by its suffix", literal.Span);
    }

    internal static LiteralExpression? SignedMinimumLiteralOperand(UnaryExpression expression)
    {
        AstNode operand = expression.Operand;
        while (operand is UnaryExpression { IsPrefix: true, Operator: TokenKind.Plus } plus) operand = plus.Operand;
        return expression is { IsPrefix: true, Operator: TokenKind.Minus } &&
            operand is LiteralExpression { Token.Kind: TokenKind.LongLiteral } literal &&
            TryGetIntegerLiteralValue(literal, out BigInteger value) && value == BigInteger.One << 63
                ? literal : null;
    }

    private void CheckUnaryLiteralOperand(UnaryExpression expression)
    {
        var minimum = SignedMinimumLiteralOperand(expression);
        bool added = minimum != null && signedMinimumOperands.Add(minimum);
        try { expression.Operand.Accept(this); }
        finally { if (added) signedMinimumOperands.Remove(minimum!); }
    }
}
