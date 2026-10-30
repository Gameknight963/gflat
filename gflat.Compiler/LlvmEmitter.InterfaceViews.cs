using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool TryEmitInterfaceViewCast(CastExpression node)
    {
        if (!_typeChecker.TryGetInterfaceViewCast(node, out var view)) return false;
        if (view.BorrowValueStorage) EmitAddress(node.Operand);
        else node.Operand.Accept(this);
        string reference = Pop();
        Push(EmitCast(reference, view.SourceReference, view.ViewReference));
        return true;
    }
}
