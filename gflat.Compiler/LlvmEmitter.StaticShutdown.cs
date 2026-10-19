using gflat.ast;

namespace gflat;

public partial class LlvmEmitter
{
    private void ValidateStaticShutdownAbi()
    {
        if (_nativeExternDefinitions.TryGetValue("atexit", out var declaration) &&
            (declaration.IsVariadic || EmitAbiReturnType(declaration.ReturnType) != "i32" ||
             declaration.Parameters.Count != 1 ||
             _typeChecker.ResolveAlias(declaration.Parameters[0].Type) is not FunctionPointerTypeExpression { ParameterTypes.Count: 0 } callback ||
             _typeChecker.ResolveAlias(callback.ReturnType) is not NamedTypeExpression { Name: "void" }))
        {
            var field = _typeChecker.GetStaticStorage().First(s => NeedsCleanup(s.Field.Type)).Field;
            throw new CompileExceptions.TypeCheckException(
                "Static cleanup requires the native atexit signature 'extern int atexit(void()* callback)'", field.Span);
        }
    }
}
