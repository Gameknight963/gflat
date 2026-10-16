using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    // Registration provenance survives a repeated symbol collection pass. In
    // particular, a generated specialization appended to the compilation unit
    // is not a new source declaration in the global namespace.
    private readonly HashSet<AstNode> namespaceHiddenDeclarations = new(ReferenceEqualityComparer.Instance);
    private bool IsNamespaceVisibleDeclaration(AstNode declaration) => !namespaceHiddenDeclarations.Contains(declaration);
}
