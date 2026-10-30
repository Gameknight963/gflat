using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool TryEmitReinterpretCall(CallExpression node)
    {
        if (_typeChecker.GetReinterpretCall(node) is not { } info) return false;
        info.Operand.Accept(this);
        string value = Pop();
        string source = EmitType(info.Source), result = EmitType(info.Result);
        if (source != result)
        {
            string instruction = info.Source is PointerTypeExpression or FunctionPointerTypeExpression
                ? info.Result is PointerTypeExpression or FunctionPointerTypeExpression ? "bitcast" : "ptrtoint"
                : info.Result is PointerTypeExpression or FunctionPointerTypeExpression ? "inttoptr" : "bitcast";
            string converted = NewTemp();
            Emit($"    {converted} = {instruction} {source} {value} to {result}");
            value = converted;
        }
        if (info.Result is PointerTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false })
            GuardNonNull(value, result);
        Push(value);
        return true;
    }
}
