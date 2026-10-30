using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool TryEmitExplicitConversionCast(CastExpression node)
    {
        if (_typeChecker.GetExplicitConversionCall(node) is not { } call) return false;
        // Ordinary call lowering owns argument copying/transfers and result cleanup.
        call.Accept(this);
        return true;
    }
}
