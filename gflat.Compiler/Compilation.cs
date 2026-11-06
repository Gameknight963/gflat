using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;
using gflat.Preprocessing;

namespace gflat;

/// <summary>All source snapshots and syntax trees in a single whole-program compilation.</summary>
public sealed class Compilation
{
    public IReadOnlyList<SourceFile> Sources { get; }
    public IReadOnlyList<CompilationUnit> SyntaxTrees { get; }
    public CompilationUnit Unit { get; }
    public IReadOnlyList<string> PreprocessorDependencies { get; private set; } = [];
    public IReadOnlyList<MacroSymbol> MacroSymbols { get; private set; } = [];
    public IReadOnlyDictionary<SourceId, IReadOnlyList<Token>> SourceTokens => sourceTokens;
    private readonly Dictionary<SourceId, IReadOnlyList<Token>> sourceTokens = new();

    public Compilation(IEnumerable<SourceFile> sources, DiagnosticBag diagnostics, bool recoverErrors = false, CompilationOptions? options = null,
        CancellationToken cancellationToken = default, SourceTokenCache? tokenCache = null, bool captureTokens = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var captured = new List<SourceFile>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            captured.Add(source);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var roots = captured.OrderBy(s => s.Path, StringComparer.Ordinal).ToArray();
        if (roots.Length == 0) throw new ArgumentException("Specify at least one source file.");
        if (roots.Select(s => s.Id).Distinct().Count() != roots.Length)
            throw new ArgumentException("A source path may only occur once in a compilation.");
        var allSources = roots.ToDictionary(s => s.Id);
        var dependencies = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var macroSymbols = new List<MacroSymbol>();
        var loader = options?.IncludeLoader;
        var preprocessingOptions = (options ?? new()) with { IncludeLoader = path =>
            roots.FirstOrDefault(s => !s.Path.StartsWith('<') && string.Equals(Path.GetFullPath(s.Path), path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) ?? loader?.Invoke(path) };
        void Register(PreprocessingResult result)
        {
            foreach (var file in result.Sources)
            {
                allSources[file.Id] = file;
                if (captureTokens) sourceTokens[file.Id] = (tokenCache?.Tokenize(file, new DiagnosticBag()) ?? Lexer.Tokenize(file, new DiagnosticBag())).AsReadOnly();
            }
            dependencies.UnionWith(result.Dependencies);
            macroSymbols.AddRange(result.Symbols);
        }
        var includedByRoot = new Dictionary<SourceId, SourceId[]>();
        SyntaxTrees = roots.Select(source =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Token> tokens;
            if (Preprocessor.IsNeeded(source, preprocessingOptions))
            {
                var preprocessor = new Preprocessor(preprocessingOptions, diagnostics, cancellationToken);
                try { tokens = preprocessor.Process(source).Tokens; }
                catch (TypeCheckException error) when (recoverErrors)
                {
                    diagnostics.Report(DiagnosticRules.GF0004_SyntaxError, error.Span, error.Description);
                    tokens = Lexer.Tokenize(new SourceFile(source.Path, ""));
                }
                finally
                {
                    Register(new([], preprocessor.Sources, preprocessor.Dependencies, preprocessor.Symbols));
                    includedByRoot[source.Id] = preprocessor.Sources.Select(file => file.Id).ToArray();
                }
            }
            else
            {
                tokens = tokenCache?.Tokenize(source, recoverErrors ? diagnostics : null)
                    ?? Lexer.Tokenize(source, recoverErrors ? diagnostics : null);
                if (captureTokens) sourceTokens[source.Id] = tokens.AsReadOnly();
            }
            cancellationToken.ThrowIfCancellationRequested();
            var tree = Parser.Parse(tokens, diagnostics, recoverErrors);
            tree.Span = source.Span(0, source.Text.Length);
            cancellationToken.ThrowIfCancellationRequested();
            return tree;
        }).ToArray();
        Unit = new CompilationUnit(new(), SyntaxTrees.SelectMany(t => t.Namespaces).ToList(), SyntaxTrees.SelectMany(t => t.Members).ToList(), 0);
        foreach (var tree in SyntaxTrees)
        {
            var rootId = tree.Span.SourceId!.Value;
            var imports = tree.Usings.Select(u => u.Name).ToArray();
            Unit.FileUsings[rootId] = imports;
            foreach (var includedId in includedByRoot.GetValueOrDefault(rootId) ?? [])
                Unit.FileUsings.TryAdd(includedId, imports);
        }
        try { if (options?.NoStdLib != true) AddStandardLibrary(Unit, diagnostics, allSources.Values.ToArray(), cancellationToken, tokenCache, captureTokens ? sourceTokens : null, preprocessingOptions, Register); }
        catch (TypeCheckException error) when (recoverErrors)
        { diagnostics.Report(DiagnosticRules.GF1000_GeneralTypeError, error.Span, error.Description); }
        Sources = allSources.Values.ToArray();
        PreprocessorDependencies = dependencies.ToArray();
        MacroSymbols = macroSymbols.ToArray();
        options?.DependencyObserver?.Invoke(PreprocessorDependencies);
        cancellationToken.ThrowIfCancellationRequested();
    }

    public static void AddStandardLibrary(CompilationUnit unit, DiagnosticBag diagnostics, IEnumerable<SourceFile>? inputs = null,
        CancellationToken cancellationToken = default, SourceTokenCache? tokenCache = null,
        IDictionary<SourceId, IReadOnlyList<Token>>? sourceTokens = null, CompilationOptions? options = null,
        Action<PreprocessingResult>? register = null)
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
                ns.Members.RemoveAll(m => m.Span.Source?.Path == bundledBadAlloc.Path && m.Span.Source.Text == bundledBadAlloc.Text);
        }
        bool hasException = unit.Members.OfType<ClassDeclaration>().Any(c => c.Name == "Exception");
        foreach (string relative in StandardLibrary.AutomaticFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (files.Any(f => StandardLibrary.IsSource(f.Path, relative))) continue;
            if (relative == "core/Exception.gf" && hasException) continue;
            if (relative == "core/BadAllocException.gf" && !includeBadAlloc) continue;
            var source = StandardLibrary.Load(relative);
            List<Token> tokens;
            if (Preprocessor.IsNeeded(source, options))
            {
                var processed = new Preprocessor(options, diagnostics, cancellationToken).Process(source);
                register?.Invoke(processed);
                tokens = processed.Tokens;
            }
            else tokens = tokenCache?.Tokenize(source, null) ?? Lexer.Tokenize(source);
            if (sourceTokens != null && !sourceTokens.ContainsKey(source.Id)) sourceTokens[source.Id] = tokens.AsReadOnly();
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
