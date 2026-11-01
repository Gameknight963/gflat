using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool EmitReflectionOperation(AstNode node)
    {
        if (_typeChecker.GetReflectionOperation(node) is not { } operation) return false;
        operation.Accept(this);
        return true;
    }
    public void Visit(ConstantExpression node)
    {
        if (!TryEmitConstValue(node.Value, node.Type))
            throw new InvalidOperationException("A compiler-only constant cannot be emitted into runtime storage");
    }

    public void Visit(SpliceExpression node)
    {
        if (EmitFunctionValue(node)) return;
        if (node.Resolved == null) throw new InvalidOperationException("Unbound reflection splice");
        node.Resolved.Accept(this);
    }

    public void Visit(SpliceTypeExpression node) { }

    public void Visit(ReflectExpression node)
        => throw new InvalidOperationException("A reflection value has no runtime representation");
}
