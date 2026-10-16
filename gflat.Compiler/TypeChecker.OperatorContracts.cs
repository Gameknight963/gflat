using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool IsStaticFunctionContext => _currentFunction is MethodDeclaration { IsStatic: true } or OperatorDeclaration;

    private void ValidateOperatorContract(OperatorDeclaration operation)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (ResolveAlias(parameter.Type) is ReferenceTypeExpression || parameter.IsConst || parameter.IsExtensionReceiver || parameter.DefaultValue != null)
                throw new TypeCheckException("Operator operands must be ordinary value parameters without const, ref, this, or default arguments", parameter.Span);
        }
        string signature = OverloadKey(operation.Parameters);
        var duplicate = _currentStruct!.Operators.FirstOrDefault(other => !ReferenceEquals(other, operation) &&
            other.OperatorKind == operation.OperatorKind && OverloadKey(other.Parameters) == signature);
        if (duplicate != null)
            DuplicateDeclaration(duplicate, operation, "Operator overloads cannot differ only by aliases, readonly, return type, or declaration modifiers");
    }

    private void ValidateOperatorAccess(OperatorDeclaration operation, string owner, AstNode use)
    {
        string? accessor = _currentClass?.Name ?? _currentStruct?.Name;
        bool privateAccess = CanAccessPrivate(accessor, owner);
        if (operation.Accessibility == TokenKind.Private && !privateAccess ||
            operation.Accessibility == TokenKind.Protected && !privateAccess &&
            (_currentClass == null || !IsSubclassOf(_currentClass.Name, owner)))
            throw new TypeCheckException($"Cannot access {operation.Accessibility.ToString().ToLowerInvariant()} operator '{operation.OperatorSymbol}' of '{DisplayName(owner)}'", use.Span);
    }
}
