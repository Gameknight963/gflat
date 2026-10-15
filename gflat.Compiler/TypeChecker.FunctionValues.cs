using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<AstNode> selectedFunctionValues = new();

    private bool IsOverloadedFunctionValue(AstNode expression) =>
        _functionAddressTargets.TryGetValue(expression, out var target) && target is MethodDeclaration method &&
        overloadFamilies.ContainsKey(method);

    private MethodDeclaration[] MatchingFunctionValues(MethodDeclaration method, FunctionPointerTypeExpression expected, int line)
        => overloadFamilies[method].Where(candidate => TypesMatch(FunctionValueType(candidate, line),
            new FunctionPointerTypeExpression(expected.ReturnType, expected.ParameterTypes, false, false, expected.Line))).ToArray();

    private bool CanSelectFunctionValue(AstNode expression, TypeExpression expected)
        => ResolveAlias(expected) is FunctionPointerTypeExpression { IsSignature: false, IsManaged: false } signature &&
            _functionAddressTargets[expression] is MethodDeclaration method && MatchingFunctionValues(method, signature, expression.Line).Length == 1;

    private void ValidateFunctionValueSelections()
    {
        foreach (var (expression, target) in _functionAddressTargets)
            if (!selectedFunctionValues.Contains(expression) && target is MethodDeclaration method &&
                overloadFamilies.TryGetValue(method, out var family) && family.Count > 1)
                throw new TypeCheckException($"Overloaded function '{method.Name}' requires a target function pointer signature", expression.Span);
    }

    private bool TryBindFunctionValue(AstNode expression)
    {
        AstNode? target = null;
        if (expression is IdentifierExpression id)
            target = (AstNode?)ResolveFunction(id.Name) ?? ResolveExtern(id.Name);
        else if (expression is NamespaceAccessExpression access)
        {
            target = ResolveStaticMethod(access, deferSelection: true);
            if (target == null && ResolveNamespace(access.Left) is { } scope)
                target = (AstNode?)scope.Functions.GetValueOrDefault(access.Member) ?? scope.Externs.GetValueOrDefault(access.Member);
        }
        if (target == null) return false;
        ValidateFunctionAddress(target, expression.Line);
        _functionAddressTargets[expression] = target;
        RecordType(expression, FunctionValueType(target, expression.Line));
        return true;
    }

    private FunctionPointerTypeExpression FunctionValueType(AstNode target, int line) => target switch
    {
        MethodDeclaration m => new(ResolveAlias(m.ReturnType), m.Parameters.Select(p => ResolveAlias(p.Type)).ToList(), false, false, line),
        ExternDeclaration e => new(ResolveAlias(e.ReturnType), e.Parameters.Select(p => ResolveAlias(p.Type)).ToList(), false, false, line),
        _ => throw new InvalidOperationException()
    };

    private bool SelectFunctionValue(AstNode expression, FunctionPointerTypeExpression expected)
    {
        var target = _functionAddressTargets[expression];
        if (target is not MethodDeclaration method || !overloadFamilies.TryGetValue(method, out var family)) return true;
        var matches = MatchingFunctionValues(method, expected, expression.Line);
        if (matches.Length != 1) return false;
        NamespaceAccessExpression? name = expression switch {
            NamespaceAccessExpression access => access,
            UnaryExpression { Operand: NamespaceAccessExpression access } => access,
            _ => null
        };
        if (name != null) ResolveStaticMethod(name, selectedValue: matches[0]);
        ValidateFunctionAddress(matches[0], expression.Line);
        _functionAddressTargets[expression] = matches[0];
        selectedFunctionValues.Add(expression);
        RecordType(expression, FunctionValueType(matches[0], expression.Line));
        return true;
    }
}
