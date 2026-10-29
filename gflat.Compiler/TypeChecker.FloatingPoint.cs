using gflat.ast;
using gflat.diagnostics;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<BinaryExpression, TypeExpression> _floatingOperands = new();

    public bool TryGetFloatingOperands(BinaryExpression expression, out TypeExpression type)
        => _floatingOperands.TryGetValue(expression, out type!);

    private bool CheckFloatingBinary(BinaryExpression expression, TypeExpression left, TypeExpression right)
    {
        bool leftFloat = ResolveAlias(left) is NamedTypeExpression { Name: "float" or "double" };
        bool rightFloat = ResolveAlias(right) is NamedTypeExpression { Name: "float" or "double" };
        if (!leftFloat && !rightFloat) return false;
        bool comparison = expression.Operator is TokenKind.EqualsEquals or TokenKind.NotEquals or
            TokenKind.Less or TokenKind.LessEquals or TokenKind.Greater or TokenKind.GreaterEquals;
        bool arithmetic = expression.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Star or TokenKind.Slash or TokenKind.Percent;
        if (!leftFloat || !rightFloat || !comparison && !arithmetic)
        {
            ReportError(DiagnosticRules.GF1002_BinaryOperatorMismatch, expression.Line, 0,
                OperatorText(expression.Operator), DisplayTypeName(left), DisplayTypeName(right));
            RecordType(expression, Error);
            return true;
        }
        var common = new NamedTypeExpression(ResolveAlias(left) is NamedTypeExpression { Name: "double" } ||
            ResolveAlias(right) is NamedTypeExpression { Name: "double" } ? "double" : "float", null, expression.Line);
        _floatingOperands[expression] = common;
        RecordType(expression, comparison ? Bool : common);
        return true;
    }
}
