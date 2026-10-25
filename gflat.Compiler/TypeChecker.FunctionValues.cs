using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private readonly HashSet<AstNode> selectedFunctionValues = new();
    private readonly Dictionary<AstNode, IReadOnlyList<TypeExpression>> selectedGenericFunctionValueArguments = new();

    private bool IsOverloadedFunctionValue(AstNode expression) =>
        _functionAddressTargets.TryGetValue(expression, out var target) && target is MethodDeclaration method &&
        (method.IsGeneric || overloadFamilies.ContainsKey(method));

    private CallableOverloadCandidate[] MatchingFunctionValues(MethodDeclaration method, FunctionPointerTypeExpression expected, int line, AstNode expression)
    {
        var matches = new List<CallableOverloadCandidate>();
        foreach (var candidate in overloadFamilies.TryGetValue(method, out var family) ? family : [method])
        {
            if (candidate.Parameters.Count != expected.ParameterTypes.Count) continue;
            using var context = EnterMethodSignatureContext(candidate);
            var names = candidate.GenericParameters.Select(parameter => parameter.Name).ToHashSet();
            var bindings = new Dictionary<string, TypeExpression>();
            if (candidate.IsGeneric)
            {
                bool consistent = InferCallableBindings(GenericFormalType(candidate, candidate.ReturnType), expected.ReturnType, names, bindings);
                for (int i = 0; consistent && i < candidate.Parameters.Count; i++)
                    consistent = InferCallableBindings(GenericFormalType(candidate, candidate.Parameters[i].Type), expected.ParameterTypes[i], names, bindings);
                if (!consistent || candidate.GenericParameters.Any(parameter => !bindings.ContainsKey(parameter.Name))) continue;
                if (candidate.GenericParameters.Any(parameter => parameter.Constraint != null &&
                    !FunctionConstraintSatisfied(candidate, bindings[parameter.Name],
                        SubstituteExtensionType(GenericFormalType(candidate, parameter.Constraint), bindings)))) continue;
            }
            var parameters = candidate.Parameters.Select(parameter => new Parameter(parameter.Name,
                CallablePreviewType(candidate, BindCallableOwnerType(candidate, expression,
                    SubstituteExtensionType(GenericFormalType(candidate, parameter.Type), bindings))),
                parameter.Line, parameter.IsConst, parameter.DefaultValue)).ToList();
            var returnType = CallablePreviewType(candidate, BindCallableOwnerType(candidate, expression,
                SubstituteExtensionType(GenericFormalType(candidate, candidate.ReturnType), bindings)));
            if (!TypesMatch(OverloadArgumentType(returnType), OverloadArgumentType(expected.ReturnType)) ||
                !parameters.Zip(expected.ParameterTypes).All(pair => TypesMatch(OverloadArgumentType(pair.First.Type), OverloadArgumentType(pair.Second)))) continue;
            matches.Add(new(candidate, candidate.GenericParameters.Select(parameter => bindings[parameter.Name]).ToList(), parameters));
        }
        return matches.Where(candidate => !matches.Any(other => !ReferenceEquals(candidate, other) &&
            PreferCallableOverload(other, candidate))).ToArray();
    }

    private bool CanSelectFunctionValue(AstNode expression, TypeExpression expected)
        => ResolveAlias(expected) is FunctionPointerTypeExpression { IsSignature: false, IsManaged: false } signature &&
            _functionAddressTargets[expression] is MethodDeclaration method && MatchingFunctionValues(method, signature, expression.Line, expression).Length == 1;

    private void ValidateFunctionValueSelections()
    {
        foreach (var (expression, target) in _functionAddressTargets)
        {
            if (!selectedFunctionValues.Contains(expression) && target is MethodDeclaration method &&
                (method.IsGeneric || overloadFamilies.TryGetValue(method, out var family) && family.Count > 1))
                throw new TypeCheckException($"Overloaded function '{method.Name}' requires a target function pointer signature", expression.Span);
            if (target is MethodDeclaration selected) ValidateNamespaceFunctionAccess(selected, expression.Span);
        }
    }

    private bool TryBindFunctionValue(AstNode expression)
    {
        var name = expression is UnaryExpression { Operator: TokenKind.Ampersand } address ? address.Operand : expression;
        AstNode? target = null;
        if (name is IdentifierExpression id)
            target = (AstNode?)ResolveFunction(id.Name) ?? ResolveGenericFunction(id.Name) ?? (AstNode?)ResolveExtern(id.Name);
        else if (name is NamespaceAccessExpression access)
        {
            target = ResolveStaticMethod(access, deferSelection: true);
            if (target == null && ResolveNamespace(access.Left) is { } scope)
                target = (AstNode?)scope.Functions.GetValueOrDefault(access.Member) ?? scope.GenericFunctions.GetValueOrDefault(access.Member) ?? (AstNode?)scope.Externs.GetValueOrDefault(access.Member);
        }
        if (target == null) return false;
        bool needsTarget = target is MethodDeclaration method && (method.IsGeneric || overloadFamilies.ContainsKey(method));
        if (!needsTarget) ValidateFunctionAddress(target, expression.Line);
        _functionAddressTargets[expression] = target;
        // A method group has no function signature until a target selects it.
        RecordType(expression, needsTarget ? new FunctionPointerTypeExpression(Void, [], false, false, expression.Line)
            : FunctionValueType(target, expression.Line, expression));
        return true;
    }

    private FunctionPointerTypeExpression FunctionValueType(AstNode target, int line, AstNode expression) => target switch
    {
        MethodDeclaration m => new(BindCallableOwnerType(m, expression, ResolveAlias(m.ReturnType)),
            m.Parameters.Select(p => BindCallableOwnerType(m, expression, ResolveAlias(p.Type))).ToList(), false, false, line),
        ExternDeclaration e => new(ResolveAlias(e.ReturnType), e.Parameters.Select(p => ResolveAlias(p.Type)).ToList(), false, false, line),
        _ => throw new InvalidOperationException()
    };

    private bool SelectFunctionValue(AstNode expression, FunctionPointerTypeExpression expected)
    {
        var target = _functionAddressTargets[expression];
        if (target is not MethodDeclaration method) return true;
        if (!method.IsGeneric && !overloadFamilies.ContainsKey(method))
        {
            ValidateNamespaceFunctionAccess(method, expression.Span);
            return true;
        }
        var matches = MatchingFunctionValues(method, expected, expression.Line, expression);
        if (matches.Length != 1) return false;
        NamespaceAccessExpression? name = expression switch {
            NamespaceAccessExpression access => access,
            UnaryExpression { Operand: NamespaceAccessExpression access } => access,
            _ => null
        };
        var selected = matches[0].Definition;
        if (name != null) ResolveStaticMethod(name, selectedValue: selected);
        ValidateNamespaceFunctionAccess(selected, expression.Span);
        var genericArguments = selected.IsGeneric ? matches[0].TypeArguments : null;
        if (selected.IsGeneric) selected = MonomorphizeFunction(selected, matches[0].TypeArguments, expression.Line);
        ValidateFunctionAddress(selected, expression.Line);
        if (genericArguments != null) selectedGenericFunctionValueArguments[expression] = genericArguments.ToArray();
        else selectedGenericFunctionValueArguments.Remove(expression);
        _functionAddressTargets[expression] = selected;
        selectedFunctionValues.Add(expression);
        RecordType(expression, new FunctionPointerTypeExpression(expected.ReturnType,
            expected.ParameterTypes, false, false, expression.Line));
        return true;
    }
}
