namespace gflat;

public partial class LlvmEmitter
{
    private void EmitCallInstruction(string returnType, string target, string arguments,
        string? result = null, bool canUnwind = true, string? funclet = null)
    {
        string prefix = result == null ? "    " : $"    {result} = ";
        string invocation = $"{returnType} {target}({arguments})";
        if (!canUnwind || _functions.Count == 0 ||
            (!_typeChecker.UsesCollector && !CurrentFunction.InCleanup && !CurrentFunction.ExceptionBoundary &&
             _emitterTryStack.Count == 0 && !_deferScopes.Any(scope => scope.Count != 0)))
        {
            string bundle = funclet == null ? "" : $" [ \"funclet\"(token {funclet}) ]";
            Emit(prefix + "call " + invocation + bundle);
            return;
        }
        EmitNativeInvoke(prefix, invocation);
    }
}
