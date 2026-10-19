using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // Checking a lambda or a generic specialization can be nested inside a
    // caller's try block. Its handlers belong only to the caller's function.
    private IDisposable IsolateExceptionHandlers() => new ExceptionHandlerScope(this);
    private sealed class ExceptionHandlerScope : IDisposable
    {
        private readonly Stack<TryStatement> stack;
        private readonly TypeChecker owner;
        private readonly int catchDepth;
        private readonly TryStatement[] saved;
        public ExceptionHandlerScope(TypeChecker owner)
        {
            this.owner = owner;
            catchDepth = owner._catchDepth;
            owner._catchDepth = 0;
            stack = owner._tryStack;
            saved = stack.ToArray();
            stack.Clear();
        }
        public void Dispose()
        {
            owner._catchDepth = catchDepth;
            stack.Clear();
            foreach (var handler in saved.Reverse()) stack.Push(handler);
        }
    }
    private readonly HashSet<MethodDeclaration> _exceptionMethods = new();
    private readonly Dictionary<MethodDeclaration, HashSet<string>> _directExceptions = new();
    private readonly List<(MethodDeclaration Caller, CallExpression Call, string[] Catches)> _exceptionEdges = new();
    private Dictionary<MethodDeclaration, HashSet<string>>? _exceptionSummaries;

    public const string UnknownExceptionType = "<unknown>";

    // Unknown dispatch is kept separate from a known throw of Exception.
    public IReadOnlyList<string> GetPossibleExceptions(MethodDeclaration method)
    {
        if (method.IsNoThrow) return [];
        if (_exceptionSummaries == null)
        {
            var summaries = _exceptionMethods.ToDictionary(m => m,
                m => new HashSet<string>(_directExceptions.GetValueOrDefault(m) ?? []));
            bool changed;
            do
            {
                changed = false;
                foreach (var (caller, call, catches) in _exceptionEdges)
                {
                    if (caller.IsNoThrow || !summaries.TryGetValue(caller, out var result)) continue;
                    var declaration = GetResolvedCall(call);
                    if (declaration is MethodDeclaration { IsNoThrow: true } or ExternDeclaration { IsNoThrow: true }) continue;
                    var target = declaration as MethodDeclaration;
                    IEnumerable<string> types = target is { IsVirtual: false, IsOverride: false, IsAbstract: false } && summaries.TryGetValue(target, out var known)
                        ? known.ToArray() : new[] { UnknownExceptionType };
                    foreach (string type in types)
                        if (!catches.Any(c => c == "Exception" || c == type || type != UnknownExceptionType && TypeDerivesFromClass(NamedTypeExpression.Resolved(type, 0), c)))
                            changed |= result.Add(type);
                }
            } while (changed);
            _exceptionSummaries = summaries;
        }
        return _exceptionSummaries.TryGetValue(method, out var summary)
            ? summary.OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : !method.IsNoThrow ? [UnknownExceptionType] : [];
    }

    private void RecordExceptionCall(CallExpression call)
    {
        if (_currentFunction is not MethodDeclaration caller) return;
        var catches = new List<string>();
        foreach (var clause in _tryStack.SelectMany(t => t.CatchClauses))
        {
            if (clause.ExceptionType == null) { catches.Add("Exception"); continue; }
            TypeExpression type = ResolveAlias(clause.ExceptionType);
            if (type is PointerTypeExpression pointer) type = ResolveAlias(pointer.Inner);
            if (type is ReferenceTypeExpression reference) type = ResolveAlias(reference.Inner);
            if (type is NamedTypeExpression named) catches.Add(named.Name);
        }
        _exceptionEdges.Add((caller, call, catches.ToArray()));
        _exceptionSummaries = null;
    }
}
