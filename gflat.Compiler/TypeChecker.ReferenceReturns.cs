using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private bool IsLocalOrTemporaryReferenceStorage(AstNode expression)
    {
        if (FindProperty(expression)?.Getter?.IsRefReturn == true) return false;
        return expression switch {
            IdentifierExpression identifier => _scopes.Any(scope => scope.ContainsKey(identifier.Name)),
            NamespaceAccessExpression => false,
            UnaryExpression { Operator: TokenKind.Star } => false,
            MemberAccessExpression member => ResolveAlias(GetType(member.Object)) is not (PointerTypeExpression or ManagedTypeExpression) &&
                IsLocalOrTemporaryReferenceStorage(member.Object),
            IndexExpression index => ResolveAlias(GetType(index.Target)) is ArrayTypeExpression &&
                IsLocalOrTemporaryReferenceStorage(index.Target),
            _ => true
        };
    }
}
