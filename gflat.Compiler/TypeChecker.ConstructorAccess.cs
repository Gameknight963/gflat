using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void ValidateConstructorAccess(ConstructorDeclaration constructor, string owner,
        string? accessor, SourceSpan use)
    {
        bool accessible = constructor.Accessibility switch
        {
            TokenKind.Private => CanAccessPrivate(accessor, owner),
            TokenKind.Protected => accessor != null && IsSubclassOf(accessor, owner),
            _ => true
        };
        if (!accessible)
            throw new TypeCheckException($"The {constructor.Accessibility.ToString().ToLowerInvariant()} constructor of '{DisplayName(owner)}' is inaccessible", use);
    }
}
