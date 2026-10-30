using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly Dictionary<CastExpression, CallExpression> explicitConversionCalls = new();
    public CallExpression? GetExplicitConversionCall(CastExpression node) => explicitConversionCalls.GetValueOrDefault(node);

    private IEnumerable<MethodDeclaration> ExplicitConversionsOn(string owner)
        => (_structs.TryGetValue(owner, out var structure) ? structure.AllMethods :
            _classes.TryGetValue(owner, out var cls) ? cls.AllMethods : [])
            .Where(method => method.IsExplicitConversion);

    private void ValidateExplicitConversionDeclaration(MethodDeclaration method)
    {
        if (!method.IsExplicitConversion) return;
        if (!TryGetMethodOwner(method, out string owner, out _) || !method.IsStatic ||
            method.Accessibility != TokenKind.Public || method.IsGeneric || method.Parameters.Count != 1 ||
            method.Parameters[0].Type is ReferenceTypeExpression || method.Parameters[0].IsConst ||
            method.Parameters[0].IsExtensionReceiver || method.Parameters[0].DefaultValue != null ||
            method.ImplementationKind != FunctionImplementationKind.Ordinary)
            throw new TypeCheckException("An explicit conversion operator must be public static with one ordinary value parameter", method.Span);
        BindMethodSignature(method);
        TypeExpression source = ResolveAlias(method.Parameters[0].Type), target = ResolveAlias(method.ReturnType);
        TypeExpression containing = NamedTypeExpression.Resolved(owner, method.Line);
        if (!TypesMatch(source, containing) && !TypesMatch(target, containing))
            throw new TypeCheckException("An explicit conversion operator must convert from or to its containing type", method.Span);
        if (IsValidCast(source, target))
            throw new TypeCheckException("An explicit conversion operator cannot replace an existing built-in conversion", method.Span);
        foreach (var other in ExplicitConversionsOn(owner).ToArray())
        {
            if (ReferenceEquals(method, other)) continue;
            BindMethodSignature(other);
            if (other.Parameters.Count == 1 && TypesMatch(source, other.Parameters[0].Type) && TypesMatch(target, other.ReturnType))
                throw new TypeCheckException($"Duplicate explicit conversion from '{DisplayTypeName(source)}' to '{DisplayTypeName(target)}'", method.Span);
        }
    }

    private void ValidateExplicitConversionDeclarations()
    {
        foreach (var method in _structs.Values.SelectMany(owner => owner.AllMethods).Concat(
            _classes.Values.SelectMany(owner => owner.AllMethods)).Where(method => method.IsExplicitConversion).ToArray())
            ValidateExplicitConversionDeclaration(method);
    }

    private bool TryCheckExplicitConversionCast(CastExpression node, TypeExpression source, TypeExpression target)
    {
        var owners = new HashSet<string>(StringComparer.Ordinal);
        if (ResolveAlias(source) is NamedTypeExpression from && (_structs.ContainsKey(from.Name) || _classes.ContainsKey(from.Name))) owners.Add(from.Name);
        if (ResolveAlias(target) is NamedTypeExpression to && (_structs.ContainsKey(to.Name) || _classes.ContainsKey(to.Name))) owners.Add(to.Name);
        var candidates = new List<(string Owner, MethodDeclaration Method)>();
        foreach (string owner in owners)
            foreach (var method in ExplicitConversionsOn(owner).ToArray())
            {
                ValidateExplicitConversionDeclaration(method);
                if (TypesMatch(source, method.Parameters[0].Type) && TypesMatch(target, method.ReturnType)) candidates.Add((owner, method));
            }
        if (candidates.Count == 0) return false;
        if (candidates.Count != 1)
            throw new TypeCheckException($"Explicit conversion from '{DisplayTypeName(source)}' to '{DisplayTypeName(target)}' is ambiguous", node.Span);
        var selected = candidates[0];
        var callee = new NamespaceAccessExpression(NamedTypeExpression.Resolved(selected.Owner, node.Line), selected.Method.Name, node.Line) { Span = node.Span };
        var call = new CallExpression(callee, [node.Operand], node.Line) { Span = node.Span };
        // This is the same lowered-argument seam used by extension calls. The
        // cast already checked its operand; method selection must not replay it.
        checkedExtensionArguments.Add(call);
        call.Accept(this);
        explicitConversionCalls[node] = call;
        RecordType(node, node.TargetType);
        return true;
    }
}

