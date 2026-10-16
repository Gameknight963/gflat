using gflat.ast;
using gflat.CompileExceptions;

namespace gflat;

public partial class TypeChecker
{
    private void ValidateNamespaceFunctionAccess(MethodDeclaration method, SourceSpan use)
    {
        if (method.Accessibility == TokenKind.Private && method.Span.SourceId != use.SourceId &&
            _functionNamespaces.ContainsKey(method) && !TryGetMethodOwner(method, out _, out _))
            throw new TypeCheckException($"Namespace function '{DisplayName(method.Name)}' is private to its source file", use);
    }
}
