using System.Numerics;
using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private bool TryEvaluateReinterpretCall(CallExpression node)
    {
        if (_context is not TypeChecker checker || checker.GetReinterpretCall(node) is not { } info) return false;
        CheckSteps(node.Line);
        if (info.Result is ReferenceTypeExpression)
            throw new ConstEvalException("Storage reinterpretation requires runtime evaluation", node.Line);
        info.Operand.Accept(this);
        ConstValue value = _currentValue ?? throw new ConstEvalException("Missing reinterpret operand", node.Line);
        if (info.Source is PointerTypeExpression or FunctionPointerTypeExpression ||
            info.Result is PointerTypeExpression or FunctionPointerTypeExpression)
        {
            // Symbolic constant addresses cannot be converted to host-process integers.
            if (info.Source is PointerTypeExpression && info.Result is PointerTypeExpression &&
                value is ConstValue.Pointer or ConstValue.ArrayPointer or ConstValue.StaticAddress or ConstValue.String)
            {
                _currentValue = value;
                return true;
            }
            if (ConstValue.TryIntegerValue(value, out BigInteger address) && address == 0 &&
                info.Result is PointerTypeExpression { IsNullable: true } or FunctionPointerTypeExpression { IsNullable: true })
            {
                _currentValue = new ConstValue.Integer(0);
                return true;
            }
            throw new ConstEvalException("Address reinterpretation requires runtime storage", node.Line);
        }
        // The only non-address value conversion is an integral/enum identity.
        _currentValue = value;
        return true;
    }
}
