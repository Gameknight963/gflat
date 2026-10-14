using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class LlvmEmitter
{
    private string ConstArrayAddress(ConstValue.ArrayPointer value, string expectedPointerType)
    {
        if (value.Target.Elements.Count == 0)
            throw new InvalidOperationException("An empty constant array has no element address");
        var type = new ArrayTypeExpression(value.ElementType, value.Target.Elements.Count, value.ElementType.Line);
        string arrayType = EmitType(type);
        if (!_emittedConstGlobals.TryGetValue(value, out var global))
        {
            global = NewGlobal();
            _emittedConstGlobals[value] = global;
            EmitGlobal($"{global} = internal constant {EmitConstInitializer(value.Target, type)}");
        }
        string address = $"getelementptr inbounds ({arrayType}, {arrayType}* {global}, i32 0, i32 0)";
        string elementPointerType = EmitType(value.ElementType) + "*";
        return elementPointerType == expectedPointerType ? address :
            $"bitcast ({elementPointerType} {address} to {expectedPointerType})";
    }
}
