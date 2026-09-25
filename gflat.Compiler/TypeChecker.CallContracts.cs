using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void ValidateFunctionAddress(AstNode function, int line)
    {
        if (function is MethodDeclaration { Throws: true })
            throw new TypeCheckException("Taking the address of a throwing function is not supported: function pointers cannot represent throws", line);
        var parameters = function switch
        {
            MethodDeclaration m => m.Parameters,
            ExternDeclaration e => e.Parameters,
            _ => []
        };
        if (parameters.Any(p => p.IsConst))
            throw new TypeCheckException("Taking the address of a function with const parameters is not supported", line);
        if (function is ExternDeclaration { IsVariadic: true })
            throw new TypeCheckException("Taking the address of a variadic function is not supported", line);
        if (function is MethodDeclaration { IsGeneric: true })
            throw new TypeCheckException("Taking the address of an unspecialized generic function is not supported", line);
    }
}
