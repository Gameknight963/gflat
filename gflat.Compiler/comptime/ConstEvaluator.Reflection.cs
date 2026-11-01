using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    public void Visit(ReflectExpression node)
    {
        CheckSteps(node.Line);
        if (_context.TryGetReflectionTarget(node, out var target))
        {
            _currentValue = target;
            return;
        }
        node.Target.Accept(this);
        var value = _currentValue!;
        var type = _context.GetType(node.Target);
        _currentValue = new ConstValue.Reflection("value:" + _context.ReflectType(type, node.Line).Identity, "Value", "", type, Value: value);
    }

    public void Visit(ConstantExpression node)
    {
        CheckSteps(node.Line);
        _currentValue = node.Value;
    }

    public void Visit(SpliceExpression node)
    {
        CheckSteps(node.Line);
        if (node.Resolved == null) throw new ConstEvalException("Splice has not been bound", node.Line);
        node.Resolved.Accept(this);
    }

    public void Visit(SpliceTypeExpression node)
        => throw new ConstEvalException("A type splice is not a value expression", node.Line);
}
