using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat;

/// <summary>All source snapshots and syntax trees in a single whole-program compilation.</summary>
public sealed class Compilation
{
    public IReadOnlyList<SourceFile> Sources { get; }
    public IReadOnlyList<CompilationUnit> SyntaxTrees { get; }
    public CompilationUnit Unit { get; }

    public Compilation(IEnumerable<SourceFile> sources, DiagnosticBag diagnostics)
    {
        Sources = sources.OrderBy(s => s.Path, StringComparer.Ordinal).ToArray();
        if (Sources.Count == 0) throw new ArgumentException("Specify at least one source file.");
        if (Sources.Select(s => s.Id).Distinct().Count() != Sources.Count)
            throw new ArgumentException("A source path may only occur once in a compilation.");
        SyntaxTrees = Sources.Select(s => Parser.Parse(Lexer.Tokenize(s), diagnostics)).ToArray();
        Unit = new CompilationUnit(new(), SyntaxTrees.SelectMany(t => t.Namespaces).ToList(), SyntaxTrees.SelectMany(t => t.Members).ToList(), 0);
        foreach (var tree in SyntaxTrees)
            Unit.FileUsings[tree.Span.SourceId!.Value] = tree.Usings.Select(u => u.Name).ToArray();
        AddPrelude(Unit, diagnostics);
    }

    public static void AddPrelude(CompilationUnit unit, DiagnosticBag diagnostics)
    {
        var reserved = unit.Members.OfType<ClassDeclaration>().FirstOrDefault(c => c.Name == "Attribute");
        if (reserved != null) throw new TypeCheckException("Attribute is a reserved prelude class", reserved.Span);
        bool hasException = unit.Members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception");
        var prelude = Parser.Parse(Lexer.Tokenize(new SourceFile("<prelude>", Prelude.Source)), diagnostics);
        unit.Members.InsertRange(0, prelude.Members.Where(m => !(hasException && m is ClassDeclaration c && c.Name == "Exception")));
        var allocator = Parser.Parse(Lexer.Tokenize(new SourceFile("<allocator>", Prelude.AllocatorSource)), diagnostics);
        unit.Namespaces.InsertRange(0, allocator.Namespaces);
        foreach (var member in allocator.Members)
            if (member is not ExternDeclaration ext || !unit.Members.OfType<ExternDeclaration>().Any(e => e.Name == ext.Name))
                unit.Members.Add(member);
    }
}
