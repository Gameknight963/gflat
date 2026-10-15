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
    public IReadOnlyDictionary<AstNode, TypeExpression> DeclaredTypes { get; }
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> Children = new();

    public AnalysisSnapshot(IEnumerable<SourceFile> sources)
    {
        var diagnostics = new DiagnosticBag();
        var nodes = new List<Syntax>();
        var declaredTypes = new Dictionary<AstNode, TypeExpression>(ReferenceEqualityComparer.Instance);
        DeclaredTypes = declaredTypes;
        Imports = new Dictionary<SourceId, string[]>();
        try
        {
            var compilation = new Compilation(sources, diagnostics, recoverErrors: true);
            Imports = new Dictionary<SourceId, string[]>(compilation.Unit.FileUsings);
            Capture(compilation.Unit, null);
            var aliases = nodes.Select(n => n.Node).OfType<AliasDeclaration>().Select(a => a.Name).ToHashSet();
            var cloner = new AstCloner(new());
            foreach (var entry in nodes)
            {
                TypeExpression? type = entry.Node switch {
                    MethodDeclaration m => m.ReturnType, ExternDeclaration e => e.ReturnType,
                    Parameter p => p.Type, FieldDeclaration f => f.Type,
                    PropertyDeclaration p => p.Type, VariableDeclaration v => v.Type, _ => null
                };
                if (type != null && ContainsAlias(type)) declaredTypes[entry.Node] = cloner.CloneType(type);
            }
            bool ContainsAlias(TypeExpression type) => type switch {
                NamedTypeExpression n => aliases.Contains(n.Name.Split("::").Last()) || n.TypeArguments.Any(ContainsAlias),
                NestedTypeExpression n => aliases.Contains(n.Member) || ContainsAlias(n.Parent) || n.TypeArguments.Any(ContainsAlias),
                PointerTypeExpression p => ContainsAlias(p.Inner),
                ManagedTypeExpression p => ContainsAlias(p.Inner) || p.Collector != null && ContainsAlias(p.Collector),
                ReferenceTypeExpression r => ContainsAlias(r.Inner),
                ArrayTypeExpression a => ContainsAlias(a.ElementType),
                FunctionPointerTypeExpression f => ContainsAlias(f.ReturnType) || f.ParameterTypes.Any(ContainsAlias),
                _ => false
            };
            Checker = new TypeChecker(diagnostics, recoverErrors: true);
            compilation.Unit.Accept(Checker);
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
