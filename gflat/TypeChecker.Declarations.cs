using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

public partial class TypeChecker
{
    private void CheckDeclarationConflicts(CompilationUnit unit)
    {
        var declarations = new Dictionary<(string Scope, string Name), AstNode>();
        Walk(unit.Members, "");
        foreach (var ns in unit.Namespaces) Walk(ns.Members, ns.Name);
        void Walk(List<AstNode> members, string scope)
        {
            foreach (var node in members)
            {
                if (node is NamespaceDeclaration ns) { Walk(ns.Members, scope + "::" + ns.Name); continue; }
                string? name = node switch
                {
                    ClassDeclaration c => c.Name, StructDeclaration s => s.Name,
                    InterfaceDeclaration i => i.Name, EnumDeclaration e => e.Name,
                    AliasDeclaration a => a.Name, FieldDeclaration f => f.Name,
                    ExternDeclaration e => e.Name, MethodDeclaration m => m.Name, _ => null
                };
                if (name == null) continue;
                if (declarations.TryGetValue((scope, name), out var previous) &&
                    !(previous is MethodDeclaration && node is MethodDeclaration))
                    DuplicateDeclaration(previous, node, $"Duplicate declaration of '{name}' in '{scope}' (including any extern declaration)");
                declarations[(scope, name)] = node;
                if (node is ClassDeclaration cls) Walk(cls.Members, scope + "::" + name);
                if (node is StructDeclaration str) Walk(str.Members, scope + "::" + name);
            }
        }
    }

    private void DuplicateDeclaration(AstNode first, AstNode second, string message)
    {
        using var context = SourceContext.Enter(second.Span);
        var diagnostic = new Diagnostic(DiagnosticRules.GF1000_GeneralTypeError, second.Line, second.Span.Column, second.Span.Source?.Path, message);
        diagnostic.RelatedLocations.Add(first.Span);
        _diagnostics.Report(diagnostic);
        throw new TypeCheckException(message, second.Span);
    }
}
