using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Dictionary<(AstNode Declaration, string Type), string> constantAddresses = new();

    private bool TryEmitConstantAddress(AstNode expression)
    {
        if (_typeChecker.GetConstantDeclaration(expression) is not { } declaration ||
            !_typeChecker.TryGetConstValue(expression, out var value) || value == null) return false;
        var type = _typeChecker.GetType(expression);
        string llvmType = EmitType(type);
        var key = (declaration, llvmType);
        if (!constantAddresses.TryGetValue(key, out string? address))
        {
            address = NewGlobal();
            constantAddresses.Add(key, address);
            EmitGlobal($"{address} = private constant {EmitConstInitializer(value, type)}");
        }
        Push(address);
        return true;
    }

    private bool TryEmitConstantArrayRead(AstNode expression)
    {
        if (_typeChecker.ResolveAlias(_typeChecker.GetType(expression)) is not ArrayTypeExpression ||
            !TryEmitConstantAddress(expression)) return false;
        string address = Pop(), value = NewTemp(), type = EmitType(_typeChecker.GetType(expression));
        Emit($"    {value} = getelementptr {type}, {type}* {address}, i32 0, i32 0");
        Push(value);
        return true;
    }
}
