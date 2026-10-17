namespace gflat;

public partial class LlvmEmitter
{
    private void EmitCallInstruction(string returnType, string target, string arguments,
        string? result = null, bool canUnwind = true, string? funclet = null)
    {
        string prefix = result == null ? "    " : $"    {result} = ";
        string invocation = $"{returnType} {target}({arguments})";
        if (!canUnwind || !_ehFunction ||
            (!_typeChecker.UsesCollector && !_ehCleanup && !_ehBoundary &&
             _emitterTryStack.Count == 0 && !_deferScopes.Any(scope => scope.Count != 0)))
        {
            string bundle = funclet == null ? "" : $" [ \"funclet\"(token {funclet}) ]";
            Raw(prefix + "call " + invocation + bundle);
            return;
        }
        EmitNativeInvoke(prefix, invocation);
    }
}
