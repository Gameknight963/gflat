using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<MethodDeclaration> _exceptionMethods = new();
    private readonly Dictionary<MethodDeclaration, HashSet<string>> _directExceptions = new();
    private readonly List<(MethodDeclaration Caller, CallExpression Call, string[] Catches)> _exceptionEdges = new();
    private Dictionary<MethodDeclaration, HashSet<string>>? _exceptionSummaries;

    // Conservative static types, including their subclasses. Exception represents
    // an unknown throwing target. This information does not change throws checking.
    public IReadOnlyList<string> GetPossibleExceptions(MethodDeclaration method)
    {
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
                    if (!summaries.TryGetValue(caller, out var result)) continue;
                    var target = GetResolvedCall(call) as MethodDeclaration;
                    IEnumerable<string> types = target is { IsVirtual: false, IsOverride: false, IsAbstract: false } && summaries.TryGetValue(target, out var known)
                        ? known.ToArray() : new[] { "Exception" };
                    foreach (string type in types)
                        if (!catches.Any(c => c == "Exception" || c == type || TypeDerivesFromClass(new NamedTypeExpression(type, null, 0), c)))
                            changed |= result.Add(type);
                }
            } while (changed);
            _exceptionSummaries = summaries;
        }
        return _exceptionSummaries.TryGetValue(method, out var summary)
            ? summary.OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : method.Throws ? ["Exception"] : [];
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
