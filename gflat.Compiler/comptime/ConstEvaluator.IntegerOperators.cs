using System.Numerics;
using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private TypeExpression NumericType(TypeExpression type)
    {
        type = _context.ResolveAlias(type);
        return type is NamedTypeExpression named && _context.ResolveEnum(named) is { } enumeration
            ? NumericType(enumeration.UnderlyingType) : type;
    }

    private (int Bits, bool Signed)? IntegerRepresentation(TypeExpression type)
        => NumericType(type) is NamedTypeExpression named ? NumericSemantics.IntegerType(named.Name) : null;

    // Character values retain their storage representation in fields and arrays,
    // but numeric operations interpret that byte as signed, like native lowering.
    private static ConstValue ArithmeticOperand(ConstValue value)
        => value is ConstValue.Char character ? new ConstValue.Integer(unchecked((sbyte)character.Value)) : value;

    private TypeExpression? IntegerArithmeticOperands(BinaryExpression expression)
    {
        if (expression.Operator is TokenKind.LessLess or TokenKind.GreaterGreater ||
            IntegerRepresentation(_context.GetType(expression.Left)) == null ||
            IntegerRepresentation(_context.GetType(expression.Right)) == null ||
            IntegerRepresentation(_context.GetType(expression)) == null) return null;
        return _context.GetType(expression);
    }

    private bool EvaluateIntegerShift(BinaryExpression expression, ConstValue left, ConstValue right)
    {
        if (expression.Operator is not (TokenKind.LessLess or TokenKind.GreaterGreater)) return false;
        if (IntegerRepresentation(_context.GetType(expression.Left)) is not { } representation)
            throw new ConstEvalException("Unsupported integer shift operand", expression.Line);
        BigInteger count = right switch
        {
            ConstValue.Integer integer => integer.Value,
            ConstValue.UInteger integer => integer.Value,
            _ => throw new ConstEvalException("Shift count must be an integer", expression.Line)
        };
        if (count < 0 || count >= representation.Bits)
            throw new ConstEvalException($"Shift count must be between 0 and {representation.Bits - 1}", expression.Line);
        int shift = (int)count;
        _currentValue = left switch
        {
            ConstValue.Integer integer => new ConstValue.Integer(expression.Operator == TokenKind.LessLess
                ? integer.Value << shift : integer.Value >> shift),
            ConstValue.UInteger integer => new ConstValue.UInteger(expression.Operator == TokenKind.LessLess
                ? integer.Value << shift : integer.Value >> shift),
            _ => throw new ConstEvalException("Shift operand must be an integer", expression.Line)
        };
        return true;
    }

    private void CheckIntegerDivision(BinaryExpression expression, ConstValue left, ConstValue right)
        => CheckIntegerDivision(expression.Operator, _context.GetType(expression), left, right, expression.Line);

    private void CheckIntegerDivision(TokenKind operation, TypeExpression type, ConstValue left, ConstValue right, int line)
    {
        if (operation is not (TokenKind.Slash or TokenKind.Percent or TokenKind.SlashEquals or TokenKind.PercentEquals) ||
            left is not ConstValue.Integer dividend || right is not ConstValue.Integer { Value: -1 } ||
            IntegerRepresentation(type) is not { Signed: true } representation) return;
        BigInteger minimum = -(BigInteger.One << (representation.Bits - 1));
        if (dividend.Value == minimum)
            throw new ConstEvalException("Signed integer division or remainder overflow", line);
    }
}
