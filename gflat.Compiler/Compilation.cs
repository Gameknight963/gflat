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
        AddStandardLibrary(Unit, diagnostics, Sources);
    }

    public static void AddStandardLibrary(CompilationUnit unit, DiagnosticBag diagnostics, IEnumerable<SourceFile>? inputs = null)
    {
        var files = inputs?.ToArray() ?? unit.Members.Select(m => m.Span.Source)
            .Concat(unit.Namespaces.Select(n => n.Span.Source)).OfType<SourceFile>().DistinctBy(s => s.Id).ToArray();
        var reserved = unit.Members.OfType<ClassDeclaration>().FirstOrDefault(c => c.Name == "Attribute" &&
            !StandardLibrary.IsSource(c.Span.Source?.Path, "core/Attribute.gf"));
        if (reserved != null) throw new TypeCheckException("Attribute is a reserved standard-library class", reserved.Span);
        bool customException = unit.Members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception" &&
            !StandardLibrary.IsSource(c.Span.Source?.Path, "core/Exception.gf"));
        if (customException)
            unit.Members.RemoveAll(m => m is ClassDeclaration { Name: "Exception" } &&
                StandardLibrary.IsSource(m.Span.Source?.Path, "core/Exception.gf"));
        bool hasException = unit.Members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception");
        foreach (string relative in StandardLibrary.AutomaticFiles)
        {
            if (files.Any(f => StandardLibrary.IsSource(f.Path, relative))) continue;
            if (relative == "core/Exception.gf" && hasException) continue;
            var source = StandardLibrary.Load(relative);
            var tree = Parser.Parse(Lexer.Tokenize(source), diagnostics);
            unit.FileUsings[source.Id] = tree.Usings.Select(u => u.Name).ToArray();
            unit.Namespaces.InsertRange(0, tree.Namespaces);
            foreach (var member in tree.Members)
                if (member is not ExternDeclaration ext || !unit.Members.OfType<ExternDeclaration>().Any(e => e.Name == ext.Name))
                    unit.Members.Add(member);
        }
        // Match the automatic-inclusion behavior when a project explicitly includes Memory.gf.
        // A user extern declaration may intentionally supply a stronger foreign contract.
        var userExterns = unit.Members.OfType<ExternDeclaration>()
            .Where(e => !StandardLibrary.IsSource(e.Span.Source?.Path, "libc/Memory.gf"))
            .Select(e => e.Name).ToHashSet();
        unit.Members.RemoveAll(m => m is ExternDeclaration e && userExterns.Contains(e.Name) &&
            StandardLibrary.IsSource(e.Span.Source?.Path, "libc/Memory.gf"));
        // Keep library type definitions ahead of user constants, as the embedded
        // prelude did. LLVM aggregate constants require their concrete type layout.
        var libraryMembers = unit.Members.Where(m => StandardLibrary.AutomaticFiles.Any(relative =>
            StandardLibrary.IsSource(m.Span.Source?.Path, relative))).ToArray();
        foreach (var member in libraryMembers) unit.Members.Remove(member);
        unit.Members.InsertRange(0, libraryMembers);
    }
}
