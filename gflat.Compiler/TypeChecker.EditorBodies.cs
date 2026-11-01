using gflat.ast;

namespace gflat;

public partial class TypeChecker
{
    private readonly IReadOnlySet<SourceId>? editorBodySources;
    private readonly HashSet<AstNode> checkedEditorBodies = new(ReferenceEqualityComparer.Instance);
    private int demandedEditorBodyDepth;

    // Partial editor snapshots contain all declarations, but only selected bodies
    // and bodies required by specialization or compile-time evaluation.
    public bool IsPartialBodyAnalysis => editorBodySources != null;

    public bool WasBodyChecked(AstNode declaration) => editorBodySources == null || checkedEditorBodies.Contains(declaration);

    private bool BeginEditorBody(AstNode declaration)
    {
        if (editorBodySources == null) return true;
        if (!ShouldCheckEditorBody(declaration)) return false;
        checkedEditorBodies.Add(declaration);
        return true;
    }

    private bool ShouldCheckEditorBody(AstNode declaration)
    {
        if (editorBodySources == null) return true;
        return demandedEditorBodyDepth != 0 ||
            declaration.Span.SourceId is { } source && editorBodySources.Contains(source);
    }

    private IDisposable DemandEditorBody() => new EditorBodyDemand(this);

    private sealed class EditorBodyDemand : IDisposable
    {
        private readonly TypeChecker checker;
        public EditorBodyDemand(TypeChecker checker) { this.checker = checker; checker.demandedEditorBodyDepth++; }
        public void Dispose() => checker.demandedEditorBodyDepth--;
    }
}
