using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private string ScalarAbiExtension(TypeExpression type)
    {
        type = _typeChecker.ResolveAlias(type);
        if (type is not NamedTypeExpression named || named.Name != "bool" && !_typeChecker.IsInteger(type)) return "";
        return TargetInfo.Default.GetIntegerExtensionAttribute(GetIntegerBitWidth(type), _typeChecker.IsUnsignedInteger(type));
    }

    // ABI attributes belong on declarations and calls, never inside LLVM types.
    private string EmitAbiParamType(TypeExpression type)
    {
        string extension = ScalarAbiExtension(type);
        return EmitParamType(type) + (extension.Length == 0 ? "" : " " + extension);
    }

    private string EmitAbiReturnType(TypeExpression type)
    {
        string extension = ScalarAbiExtension(type);
        return (extension.Length == 0 ? "" : extension + " ") + EmitType(type);
    }

    private string EmitAbiFunctionReturnType(TypeExpression type)
        => UsesManagedResultStorage(type) ? "void" : EmitAbiReturnType(type);
}
