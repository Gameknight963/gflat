using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
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
