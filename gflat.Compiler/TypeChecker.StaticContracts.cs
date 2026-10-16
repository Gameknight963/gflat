using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool TypeSatisfiesDeclaredConstraint(AstNode definition, TypeExpression argument, TypeExpression constraint,
        Action<TypeExpression>? constraintBinding = null)
    {
        using var context = SourceContext.Enter(definition.Span);
        using var locals = IsolateDeclarationContext();
        var previous = _currentNamespace;
        var previousClass = _currentClass;
        var previousStruct = _currentStruct;
        string ns = declarationNamespaces.GetValueOrDefault(definition) ??
            (definition is MethodDeclaration m ? GetFunctionNamespace(m).Replace("$", "::") : "");
        _currentNamespace = ns.Length == 0 ? _globalScope : ResolveNamespaceByName(ns)!;
        _currentClass = null;
        _currentStruct = null;
        try { return TypeSatisfiesConstraint(argument, constraint, constraintBinding); }
        finally { _currentNamespace = previous; _currentClass = previousClass; _currentStruct = previousStruct; }
    }

    private void ValidateInterfaceReference(TypeExpression type, int line)
    {
        if (ResolveAlias(type) is NamedTypeExpression named && GetInterface(named.Name) is { IsStaticContract: true })
            throw new TypeCheckException($"Static interface '{DisplayName(named.Name)}' is a type constraint, not an instance reference", line);
    }

    private static void ValidateStaticContractImplementation(MethodDeclaration contract, MethodDeclaration implementation)
    {
        if (contract.IsStatic != implementation.IsStatic)
            throw new TypeCheckException($"Implementation of '{contract.Name}' must match the interface's static modifier", implementation.Span);
        if (contract.IsStatic && implementation.Accessibility != TokenKind.Public)
            throw new TypeCheckException($"Implementation of static interface member '{contract.Name}' must be public", implementation.Span);
    }
}
