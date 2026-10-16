using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    public bool IsEntryPoint(MethodDeclaration method) => method.Name == "main" && !method.IsGeneric &&
        GetFunctionNamespace(method).Length == 0 && !TryGetMethodOwner(method, out _, out _);

    private void ValidateEntryPoint(MethodDeclaration method)
    {
        if (!IsEntryPoint(method)) return;
        if (ResolveAlias(method.ReturnType) is not NamedTypeExpression { Name: "int" })
            throw new TypeCheckException("The global main entry point must return int", method.Span);
        if (GetOverloadSuffix(method).Length != 0)
            throw new TypeCheckException("The global main entry point cannot be overloaded", method.Span);
    }
}
