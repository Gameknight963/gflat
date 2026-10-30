using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private bool TryEvaluateExplicitConversionCast(CastExpression node)
    {
        if (_context is not TypeChecker checker || checker.GetExplicitConversionCall(node) is not { } call) return false;
        call.Accept(this);
        return true;
    }
}
