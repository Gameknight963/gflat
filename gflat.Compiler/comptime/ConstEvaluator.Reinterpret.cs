using System.Numerics;
using gflat.ast;

namespace gflat.comptime;

public partial class ConstEvaluator
{
    private bool TryEvaluateReinterpretCall(CallExpression node)
    {
        if (_context is not TypeChecker checker || checker.GetReinterpretCall(node) is not { } info) return false;
        CheckSteps(node.Line);
        info.Operand.Accept(this);
        ConstValue value = _currentValue ?? throw new ConstEvalException("Missing reinterpret operand", node.Line);
        if (info.Source is PointerTypeExpression or FunctionPointerTypeExpression ||
            info.Result is PointerTypeExpression or FunctionPointerTypeExpression)
        {
            // Symbolic constant addresses cannot be converted to host-process integers.
            if (info.Source is PointerTypeExpression && info.Result is PointerTypeExpression &&
                value is ConstValue.Pointer or ConstValue.ArrayPointer or ConstValue.String)
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
        var source = (NamedTypeExpression)checker.ReinterpretScalarType(info.Source);
        var result = (NamedTypeExpression)checker.ReinterpretScalarType(info.Result);
        BigInteger bits;
        if (source.Name == "float" && value is ConstValue.Float single)
        {
            float number = (float)single.Value;
            if (float.IsNaN(number)) throw new ConstEvalException("NaN bit reinterpretation requires runtime evaluation", node.Line);
            bits = BitConverter.SingleToUInt32Bits(number);
        }
        else if (source.Name == "double" && value is ConstValue.Float wide)
            bits = BitConverter.DoubleToUInt64Bits(wide.Value);
        else
        {
            ConstValue normalized = NumericSemantics.Cast(value, source.Name, node.Line);
            if (!ConstValue.TryIntegerValue(normalized, out bits))
                throw new ConstEvalException("Expected a numeric reinterpret constant", node.Line);
            int width = checker.GetTypeSize(source) * 8;
            BigInteger modulus = BigInteger.One << width;
            bits = ((bits % modulus) + modulus) % modulus;
        }
        if (result.Name == "float")
        {
            float number = BitConverter.UInt32BitsToSingle((uint)bits);
            if (float.IsNaN(number)) throw new ConstEvalException("NaN bit reinterpretation requires runtime evaluation", node.Line);
            _currentValue = new ConstValue.Float(number);
        }
        else if (result.Name == "double")
            _currentValue = new ConstValue.Float(BitConverter.UInt64BitsToDouble((ulong)bits));
        else
            _currentValue = NumericSemantics.Cast(new ConstValue.WideInteger(bits), result.Name, node.Line);
        return true;
    }
}
