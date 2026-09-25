using System.Collections.Concurrent;
using System.Reflection;
using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

/// <summary>A source tree captured before lowering, with the corresponding checker results.
/// Retained only by editor clients; ordinary compilation does not build this index.</summary>
public sealed class AnalysisSnapshot
{
    public sealed record Syntax(AstNode Node, SourceSpan Span, Syntax? Parent);
    public IReadOnlyList<Syntax> Nodes { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public TypeChecker? Checker { get; }
    public IReadOnlyDictionary<SourceId, string[]> Imports { get; }
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Children = new();

    public AnalysisSnapshot(IEnumerable<SourceFile> sources)
    {
        var diagnostics = new DiagnosticBag();
        var nodes = new List<Syntax>();
        Imports = new Dictionary<SourceId, string[]>();
        try
        {
            var compilation = new Compilation(sources, diagnostics);
            Imports = new Dictionary<SourceId, string[]>(compilation.Unit.FileUsings);
            Capture(compilation.Unit, null);
            Checker = new TypeChecker(diagnostics);
            if (!diagnostics.HasErrors) compilation.Unit.Accept(Checker);
        }
        catch (TypeCheckException error)
        {
            if (!diagnostics.HasErrors)
                diagnostics.Report(DiagnosticRules.GF1000_GeneralTypeError, error.Span, error.Description);
        }
        Nodes = nodes;
        Diagnostics = diagnostics.Items.ToArray();

        void Capture(AstNode node, Syntax? parent)
        {
            var entry = new Syntax(node, node.Span, parent);
            nodes.Add(entry);
            // AST child properties are nodes or lists of nodes. Cache the schema, not
            // values: lowering mutates these lists, so capture before checking.
            foreach (var property in Children.GetOrAdd(node.GetType(), type => type.GetProperties()
                .Where(p => p.GetIndexParameters().Length == 0 &&
                    (typeof(AstNode).IsAssignableFrom(p.PropertyType) || typeof(IEnumerable<AstNode>).IsAssignableFrom(p.PropertyType))).ToArray()))
            {
                if (property.GetValue(node) is AstNode child) Capture(child, entry);
                else if (property.GetValue(node) is IEnumerable<AstNode> children)
                    foreach (var item in children) Capture(item, entry);
            }
        }
    }
}
