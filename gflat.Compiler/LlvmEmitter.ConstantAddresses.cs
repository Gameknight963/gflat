using gflat.ast;
using gflat.comptime;

namespace gflat;

public partial class LlvmEmitter
{
    private readonly Dictionary<(AstNode Declaration, string Type), string> constantAddresses = new();

    private bool TryEmitConstantAddress(AstNode expression)
    {
        if (_typeChecker.GetConstantDeclaration(expression) is not { } declaration) return false;
        ConstValue? value;
        if (declaration is FieldDeclaration field) value = _typeChecker.ReadConstantStorage(field);
        else if (!_typeChecker.TryGetConstValue(expression, out value) || value == null) return false;
        var type = _typeChecker.GetType(expression);
        Push(GetOrCreateConstantStorage(declaration, value, type));
        return true;
    }

    private string GetOrCreateConstantStorage(AstNode declaration, ConstValue value, TypeExpression type)
    {
        string llvmType = EmitType(type);
        var key = (declaration, llvmType);
        if (!constantAddresses.TryGetValue(key, out string? address))
        {
            address = NewGlobal();
            constantAddresses.Add(key, address);
            EmitGlobal($"{address} = private constant {EmitConstInitializer(value, type)}");
        }
        return address;
    }

    private string StaticConstantAddress(ConstValue.StaticAddress value, string expectedPointerType)
    {
        TypeExpression storageType = value.Declaration.Type;
        string storageLlvmType = EmitType(storageType);
        string address = GetOrCreateConstantStorage(value.Declaration,
            _typeChecker.ReadConstantStorage(value.Declaration), storageType);
        string pointerType = storageLlvmType + "*";
        if (value.ElementIndex is { } index)
        {
            if (_typeChecker.ResolveAlias(storageType) is not ArrayTypeExpression array)
                throw new InvalidOperationException("An indexed constant address requires array storage");
            address = $"getelementptr inbounds ({storageLlvmType}, {storageLlvmType}* {address}, i32 0, i32 {index})";
            pointerType = EmitType(array.ElementType) + "*";
        }
        return pointerType == expectedPointerType ? address :
            $"bitcast ({pointerType} {address} to {expectedPointerType})";
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
