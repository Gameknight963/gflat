using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private bool CanAccessClassMethod(MethodDeclaration method, string fallbackOwner)
    {
        string owner = TryGetMethodOwner(method, out var declaringOwner, out _) ? declaringOwner : fallbackOwner;
        return method.Accessibility switch
        {
            TokenKind.Private => CanAccessPrivate(_currentClass?.Name, owner),
            TokenKind.Protected => _currentClass != null && IsSubclassOf(_currentClass.Name, owner),
            _ => true
        };
    }

    private bool CanAccessStructMember(StructInfo owner, TokenKind accessibility)
    {
        string? current = _currentClass?.Name ?? _currentStruct?.Name;
        return accessibility switch
        {
            TokenKind.Private => CanAccessPrivate(current, owner.Name),
            TokenKind.Protected => current != null && IsSubclassOf(current, owner.Name),
            _ => true
        };
    }

    private void ValidateStructMemberAccess(StructInfo owner, string name, TokenKind accessibility, bool method, SourceSpan use)
    {
        if (!CanAccessStructMember(owner, accessibility))
            throw new TypeCheckException($"Cannot access {accessibility.ToString().ToLowerInvariant()} {(method ? "method" : "field")} '{name}' of struct '{DisplayName(owner.Name)}'", use);
    }
}
