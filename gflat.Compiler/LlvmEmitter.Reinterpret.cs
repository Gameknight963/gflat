using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private bool TryEmitReinterpretCall(CallExpression node)
    {
        if (_typeChecker.GetReinterpretCall(node) is not { } info) return false;
        if (info.Source is NamedTypeExpression { Name: "null" }) { Push("0"); return true; }
        if (info.Result is ReferenceTypeExpression reference)
        {
            EmitReinterpretReferenceAddress(info);
            string address = Pop(), loaded = NewTemp(), type = EmitType(reference.Inner);
            Emit($"    {loaded} = load {type}, {type}* {address}");
            PushManaged(node, loaded);
            return true;
        }
        TypeExpression sourceType = info.Source;
        if (info.Source is ArrayTypeExpression array)
        {
            EmitArrayReadAddress(info.Operand);
            string address = Pop(), first = NewTemp(), arrayType = EmitType(array);
            Emit($"    {first} = getelementptr {arrayType}, {arrayType}* {address}, i32 0, i32 0");
            Push(first);
            sourceType = new PointerTypeExpression(array.ElementType, false, array.Line);
        }
        else info.Operand.Accept(this);
        string value = Pop();
        string source = EmitType(sourceType), result = EmitType(info.Result);
        if (sourceType is NamedTypeExpression { Name: "null" }) value = "0";
        if (source != result)
        {
            string instruction = sourceType is PointerTypeExpression or FunctionPointerTypeExpression
                ? info.Result is PointerTypeExpression or FunctionPointerTypeExpression ? "bitcast" : "ptrtoint"
                : info.Result is PointerTypeExpression or FunctionPointerTypeExpression ? "inttoptr" : "bitcast";
            string converted = NewTemp();
            Emit($"    {converted} = {instruction} {source} {value} to {result}");
            value = converted;
        }
        if (info.Source is not (PointerTypeExpression or FunctionPointerTypeExpression or ArrayTypeExpression) &&
            (info.Result is PointerTypeExpression { IsNullable: false } or FunctionPointerTypeExpression { IsNullable: false }))
            GuardNonNull(value, result);
        Push(value);
        return true;
    }

    private void EmitReinterpretReferenceAddress(TypeChecker.ReinterpretCallInfo info)
    {
        EmitAddress(info.Operand);
        string address = Pop();
        string source = EmitType(info.Source) + "*", target = EmitType(((ReferenceTypeExpression)info.Result).Inner) + "*";
        if (source != target)
        {
            string converted = NewTemp();
            Emit($"    {converted} = bitcast {source} {address} to {target}");
            address = converted;
        }
        Push(address);
    }
}
