using System.Numerics;
using gflat.ast;
using gflat.CompileExceptions;

namespace gflat.comptime;

internal static class EnumConstantEvaluator
{
    public static void Populate(EnumDeclaration declaration, TypeChecker.EnumInfo info,
        Func<AstNode, TypeChecker.EnumInfo?> resolveEnum, Func<string, string> displayName)
    {
        if (info.UnderlyingType is not NamedTypeExpression named ||
            NumericSemantics.IntegerType(named.Name) is not { } representation)
            throw new TypeCheckException("Enum underlying type must be an integral type", declaration.Span);
        if (representation.Bits > 64)
            throw new TypeCheckException("Enum underlying types currently support at most 64 bits", declaration.Span);
        BigInteger minimum = representation.Signed ? -(BigInteger.One << (representation.Bits - 1)) : BigInteger.Zero;
        BigInteger maximum = (BigInteger.One << (representation.Signed ? representation.Bits - 1 : representation.Bits)) - 1;
        BigInteger next = 0;
        var values = new Dictionary<string, BigInteger>();
        foreach (var member in declaration.Members)
        {
            if (values.ContainsKey(member.Name))
                throw new TypeCheckException($"Enum '{declaration.SourceName}' already contains a member named '{member.Name}'", member.Span);
            if (member.Value != null) next = Evaluate(member.Value);
            if (next < minimum || next > maximum)
                throw new TypeCheckException($"Enum member '{member.Name}' value {next} is outside the range of '{named.Name}'", member.Span);
            values[member.Name] = next;
            info.Members[member.Name] = next <= long.MaxValue ? (long)next : unchecked((long)(ulong)next);
            next++;
        }

        BigInteger Referenced(TypeChecker.EnumInfo other, string member, AstNode source)
        {
            if (!other.Members.TryGetValue(member, out long value))
                throw new TypeCheckException($"Enum member '{member}' not found", source.Span);
            return other.UnderlyingType is NamedTypeExpression underlying &&
                NumericSemantics.IntegerType(underlying.Name) is { Bits: 64, Signed: false }
                    ? unchecked((ulong)value) : value;
        }

        BigInteger Evaluate(AstNode expression)
        {
            if (expression is LiteralExpression integer && TypeChecker.TryGetIntegerLiteralValue(integer, out BigInteger literal))
            {
                TypeChecker.ValidateIntegerLiteralSuffix(integer);
                return literal;
            }
            if (expression is LiteralExpression { Token.Kind: TokenKind.CharLiteral } character)
                return unchecked((sbyte)CharacterLiteralEncoding.Value(character.Token.Text, character.Line));
            if (expression is IdentifierExpression identifier && values.TryGetValue(identifier.Name, out BigInteger previous)) return previous;
            if (expression is NamespaceAccessExpression access)
            {
                if (access.Left is IdentifierExpression self && self.Name == declaration.SourceName && values.TryGetValue(access.Member, out previous)) return previous;
                if (resolveEnum(access.Left) is { } other) return Referenced(other, access.Member, access);
            }
            if (expression is MemberAccessExpression legacy && resolveEnum(legacy.Object) is { } legacyEnum)
                throw new TypeCheckException($"Enum member '{displayName(legacyEnum.Name)}::{legacy.Member}' must be accessed with '::', not '.'", legacy.Span);
            if (expression is UnaryExpression unary)
            {
                if (TypeChecker.SignedMinimumLiteralOperand(unary) != null) return -(BigInteger.One << 63);
                BigInteger operand = Evaluate(unary.Operand);
                return unary.Operator switch {
                    TokenKind.Minus => -operand,
                    TokenKind.Plus => operand,
                    _ => throw new TypeCheckException($"Operator '{unary.Operator}' does not produce an integral enum member value", unary.Span)
                };
            }
            if (expression is BinaryExpression binary)
            {
                BigInteger left = Evaluate(binary.Left), right = Evaluate(binary.Right);
                return binary.Operator switch {
                    TokenKind.Plus => left + right,
                    TokenKind.Minus => left - right,
                    TokenKind.Star => left * right,
                    TokenKind.Slash => right != 0 ? left / right : throw new TypeCheckException("Division by zero in enum member value", binary.Span),
                    TokenKind.Percent => right != 0 ? left % right : throw new TypeCheckException("Division by zero in enum member value", binary.Span),
                    TokenKind.Pipe => left | right,
                    TokenKind.Ampersand => left & right,
                    TokenKind.Caret => left ^ right,
                    TokenKind.Less => left < right ? 1 : 0,
                    TokenKind.Greater => left > right ? 1 : 0,
                    TokenKind.LessEquals => left <= right ? 1 : 0,
                    TokenKind.GreaterEquals => left >= right ? 1 : 0,
                    TokenKind.EqualsEquals => left == right ? 1 : 0,
                    TokenKind.NotEquals => left != right ? 1 : 0,
                    _ => throw new TypeCheckException($"Operator '{binary.Operator}' not supported in enum member value", binary.Span)
                };
            }
            throw new TypeCheckException("Enum member value must be a constant integer expression", expression.Span);
        }
    }
}
