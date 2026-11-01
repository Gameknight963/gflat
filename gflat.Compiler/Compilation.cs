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

    public Compilation(IEnumerable<SourceFile> sources, DiagnosticBag diagnostics, bool recoverErrors = false, CompilationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new List<SourceFile>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            captured.Add(source);
        }
        cancellationToken.ThrowIfCancellationRequested();
        Sources = captured.OrderBy(s => s.Path, StringComparer.Ordinal).ToArray();
        if (Sources.Count == 0) throw new ArgumentException("Specify at least one source file.");
        if (Sources.Select(s => s.Id).Distinct().Count() != Sources.Count)
            throw new ArgumentException("A source path may only occur once in a compilation.");
        SyntaxTrees = Sources.Select(source =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = Lexer.Tokenize(source, recoverErrors ? diagnostics : null);
            cancellationToken.ThrowIfCancellationRequested();
            var tree = Parser.Parse(tokens, diagnostics, recoverErrors);
            cancellationToken.ThrowIfCancellationRequested();
            return tree;
        }).ToArray();
        Unit = new CompilationUnit(new(), SyntaxTrees.SelectMany(t => t.Namespaces).ToList(), SyntaxTrees.SelectMany(t => t.Members).ToList(), 0);
        foreach (var tree in SyntaxTrees)
            Unit.FileUsings[tree.Span.SourceId!.Value] = tree.Usings.Select(u => u.Name).ToArray();
        try { if (options?.NoStdLib != true) AddStandardLibrary(Unit, diagnostics, Sources, cancellationToken); }
        catch (TypeCheckException error) when (recoverErrors)
        { diagnostics.Report(DiagnosticRules.GF1000_GeneralTypeError, error.Span, error.Description); }
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void AddStandardLibrary(CompilationUnit unit, DiagnosticBag diagnostics, IEnumerable<SourceFile>? inputs = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        // Legacy custom roots need not expose the standard (message, code)
        // constructor. They can supply their own Standard::BadAllocException instead.
        var customRoot = unit.Members.OfType<ClassDeclaration>().FirstOrDefault(c => c.Name == "Exception" &&
            !StandardLibrary.IsSource(c.Span.Source?.Path, "core/Exception.gf"));
        bool includeBadAlloc = customRoot == null || customRoot.Members.OfType<ConstructorDeclaration>()
            .Any(c => c.Parameters.Count >= 2 && c.Parameters.Skip(2).All(p => p.DefaultValue != null));
        if (!includeBadAlloc)
        {
            var bundledBadAlloc = StandardLibrary.Load("core/BadAllocException.gf");
            foreach (var ns in unit.Namespaces)
                ns.Members.RemoveAll(m => m is ClassDeclaration { Name: "BadAllocException" } &&
                    m.Span.Source?.Path == bundledBadAlloc.Path && m.Span.Source.Text == bundledBadAlloc.Text);
        }
        bool hasException = unit.Members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception");
        foreach (string relative in StandardLibrary.AutomaticFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Any(f => StandardLibrary.IsSource(f.Path, relative))) continue;
            if (relative == "core/Exception.gf" && hasException) continue;
            if (relative == "core/BadAllocException.gf" && !includeBadAlloc) continue;
            var source = StandardLibrary.Load(relative);
            var tokens = Lexer.Tokenize(source);
            cancellationToken.ThrowIfCancellationRequested();
            var tree = Parser.Parse(tokens, diagnostics);
            cancellationToken.ThrowIfCancellationRequested();
            unit.FileUsings[source.Id] = tree.Usings.Select(u => u.Name).ToArray();
            unit.Namespaces.InsertRange(0, tree.Namespaces);
            foreach (var member in tree.Members)
                if (member is not ExternDeclaration ext || !unit.Members.OfType<ExternDeclaration>().Any(e => e.Name == ext.Name))
                    unit.Members.Add(member);
        }
        // Keep library type definitions ahead of user constants, as the embedded
        // prelude did. LLVM aggregate constants require their concrete type layout.
        var libraryMembers = unit.Members.Where(m => StandardLibrary.AutomaticFiles.Any(relative =>
            StandardLibrary.IsSource(m.Span.Source?.Path, relative))).ToArray();
        foreach (var member in libraryMembers) unit.Members.Remove(member);
        unit.Members.InsertRange(0, libraryMembers);
    }
}
