using gflat.ast;
using gflat.CompileExceptions;
using System.Globalization;

namespace gflat;

public partial class LlvmEmitter
{
    private string IntegerLiteralConstant(LiteralExpression literal)
    {
        if (!TypeChecker.TryGetIntegerLiteralValue(literal, out var value))
            throw new TypeCheckException("Invalid integer literal", literal.Span);
        bool hexadecimal = literal.Token.Text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        bool valid = literal.Token.Kind switch {
            TokenKind.UIntLiteral => value <= uint.MaxValue,
            TokenKind.ULongLiteral => value <= ulong.MaxValue,
            TokenKind.LongLiteral => value <= (hexadecimal ? ulong.MaxValue : long.MaxValue),
            TokenKind.HexInt => value <= ulong.MaxValue,
            _ => true // Unsuffixed decimal values retain contextual widening.
        };
        if (!valid) throw new TypeCheckException("Integer literal is outside its supported representation", literal.Span);
        if (hexadecimal && literal.Token.Kind is TokenKind.HexInt or TokenKind.LongLiteral)
            return unchecked((long)(ulong)value).ToString(CultureInfo.InvariantCulture);
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private bool TryContextualIntegerLiteral(AstNode expression, TypeExpression targetType, out string value)
    {
        if (_typeChecker.ResolveAlias(targetType) is NamedTypeExpression target &&
            TypeChecker.TryGetIntegerLiteralValue(expression, out var integer) &&
            TypeChecker.FitsIntegerLiteral(target.Name, integer))
        {
            value = integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        value = "";
        return false;
    }
}
