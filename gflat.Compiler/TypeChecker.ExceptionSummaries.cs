using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // Checking a lambda or a generic specialization can be nested inside a
    // caller's try block. Its handlers belong only to the caller's function.
    private IDisposable IsolateExceptionHandlers() => new ExceptionHandlerScope(_tryStack);
    private sealed class ExceptionHandlerScope : IDisposable
    {
        private readonly Stack<TryStatement> stack;
        private readonly TryStatement[] saved;
        public ExceptionHandlerScope(Stack<TryStatement> stack)
        {
            this.stack = stack;
            saved = stack.ToArray();
            stack.Clear();
        }
        public void Dispose()
        {
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
        if (!method.Throws) return [];
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
                    if (!caller.Throws || !summaries.TryGetValue(caller, out var result)) continue;
                    var target = GetResolvedCall(call) as MethodDeclaration;
                    IEnumerable<string> types = target is { IsVirtual: false, IsOverride: false, IsAbstract: false } && summaries.TryGetValue(target, out var known)
                        ? known.ToArray() : new[] { UnknownExceptionType };
                    foreach (string type in types)
                        if (!catches.Any(c => c == "Exception" || c == type || type != UnknownExceptionType && TypeDerivesFromClass(new NamedTypeExpression(type, null, 0), c)))
                            changed |= result.Add(type);
                }
            } while (changed);
            _exceptionSummaries = summaries;
        }
        return _exceptionSummaries.TryGetValue(method, out var summary)
            ? summary.OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : method.Throws ? [UnknownExceptionType] : [];
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
            if (type is NamedTypeExpression named) catches.Add(named.Name);
        }
        _exceptionEdges.Add((caller, call, catches.ToArray()));
        _exceptionSummaries = null;
    }
}
