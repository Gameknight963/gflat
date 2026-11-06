using gflat.ast;
using System.Collections.Concurrent;
using gflat.diagnostics;
using gflat.CompileExceptions;
using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

/// <summary>Read-only editor queries over one analyzed set of source snapshots.</summary>
public sealed partial class EditorModel
{
    public static readonly string[] TokenTypes = ["namespace", "class", "struct", "interface", "enum", "type", "parameter", "variable", "property", "function", "method", "enumMember", "keyword", "modifier", "typeParameter", "macro", "preprocessor", "field", "constant", "identifier", "number", "string", "operator", "punctuation"];
    public static readonly string[] VisualStudioTokenTypes = ["namespace name", "class name", "struct name", "interface name", "enum name", "type", "parameter name", "local name", "property name", "method name", "method name", "enum member name", "keyword - control", "keyword", "type parameter name", "cppMacro", "preprocessor keyword", "field name", "constant name", "identifier", "number", "string", "operator", "punctuation"];
    public static readonly string[] TokenModifiers = ["declaration", "static", "readonly"];
    private sealed record Symbol(string Name, int Kind, string Classification, Syntax Syntax, SourceSpan NameSpan,
        string Namespace, string? Owner, Syntax? Scope, TypeExpression? Type, bool Static, TokenKind Access)
    {
        public string Qualified => Scope != null ? Name : Join(Owner ?? Namespace, Name);
        public IReadOnlyList<Parameter>? BoundParameters { get; init; }
        public TypeExpression? BoundType { get; init; }
        public IReadOnlyDictionary<string, TypeExpression>? GenericBindings { get; init; }
        public MethodDeclaration? BoundMethod { get; init; }
        public bool BoundExtension { get; init; }
        public bool IsTypeParameter { get; init; }
        public TypeExpression? TypeConstraint { get; init; }
        public IReadOnlyList<Parameter>? Parameters => IsTypeParameter ? null : BoundParameters ?? (Syntax.Node switch { MethodDeclaration m => m.Parameters, ExternDeclaration e => e.Parameters, ConstructorDeclaration c => c.Parameters, _ => null });
    }
    private readonly AnalysisSnapshot snapshot;
    private readonly IReadOnlyDictionary<MethodDeclaration, (string Owner, bool IsClass)> methodOwners;
    private bool TryMethodOwner(MethodDeclaration method, out string owner)
    {
        if (methodOwners.TryGetValue(method, out var entry)) { owner = entry.Owner; return true; }
        owner = "";
        return false;
    }
    public bool IsPartialAnalysis => snapshot.BodySources != null;
    public SourceEdit[] Format(string path, FormattingOptions options, TextRange? range = null)
        => SourceFormatter.Format(sources[path], options, range, activeNodes);
    private readonly Dictionary<string, SourceFile> sources;
    private readonly Dictionary<string, Token[]> tokens = new(Workspace.Paths);
    private readonly Dictionary<string, Syntax[]> nodes = new(Workspace.Paths);
    private readonly List<Symbol> symbols = [];
    private readonly Dictionary<string, Symbol[]> symbolsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Path, int Start, int Length), Symbol> declarationsBySpan = [];
    private readonly ConcurrentDictionary<string, Symbol[]> typeMemberFamilies = new(StringComparer.Ordinal);
    private readonly Dictionary<(Type Kind, SourceSpan Span), Symbol> sourceTypeDeclarations = [];
    private readonly Dictionary<(Type Kind, SourceSpan Span), Symbol> sourceCallableDeclarations = [];
    private readonly Dictionary<AstNode, Symbol> declarations = new(ReferenceEqualityComparer.Instance);
    private readonly ConcurrentDictionary<Token, Symbol?> references = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Syntax, int> scopeEnds = new(ReferenceEqualityComparer.Instance);
    private readonly record struct ScopeEnvironment(string Namespace, string? Owner, bool HasNamespace, bool IsStatic);
    private readonly Dictionary<Syntax, ScopeEnvironment> scopeEnvironments = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, SyntaxRangeIndex> contexts = new(Workspace.Paths);
    private readonly Dictionary<(string Path, int End), Syntax[]> callsByCalleeEnd = [];
    private readonly HashSet<Token> typePositionTokens = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Token> receiverKeywordTokens = new(ReferenceEqualityComparer.Instance);

    public EditorModel(AnalysisSnapshot snapshot, IEnumerable<SourceFile> files, CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? headerContexts = null)
    {
        using var indexTiming = PipelineTimings.Measure("editor.index");
        cancellationToken.ThrowIfCancellationRequested();
        this.snapshot = snapshot;
        methodOwners = snapshot.Checker?.CaptureMethodOwners() ?? new Dictionary<MethodDeclaration, (string, bool)>();
        headerSelections = headerContexts ?? new Dictionary<string, string>(Workspace.Paths);
        activeNodes = snapshot.Nodes.Where(n => n.Span.Source?.Path is not { } path || !headerSelections.TryGetValue(path, out string? selected) ||
            InclusionContext(n, path)?.Id == selected).ToArray();
        using (PipelineTimings.Measure("editor.index.scopes")) IndexScopeEnvironments();
        var sourceIndexTiming = PipelineTimings.Measure("editor.index.sources");
        var allFiles = files.Concat(snapshot.Sources).Concat(activeNodes.Select(n => n.Span.Source).OfType<SourceFile>())
            .DistinctBy(s => s.Path, Workspace.Paths).ToArray();
        sources = allFiles.ToDictionary(s => s.Path, Workspace.Paths);
        var nodesBySource = activeNodes.Where(n => n.Span.Source != null)
            .GroupBy(n => n.Span.Source!.Id).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var file in allFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { tokens[file.Path] = (snapshot.SourceTokens.TryGetValue(file.Id, out var capturedTokens) ? capturedTokens : Lexer.Tokenize(file, new DiagnosticBag())).Where(t => t.Kind != TokenKind.EndOfFile).ToArray(); }
            catch (TypeCheckException) { tokens[file.Path] = []; }
            nodes[file.Path] = nodesBySource.GetValueOrDefault(file.Id) ?? [];
            var braces = new Stack<int>();
            foreach (var token in tokens[file.Path])
            {
                if (token.Kind == TokenKind.OpenBrace) braces.Push(token.Start);
                else if (token.Kind == TokenKind.CloseBrace && braces.Count > 0) braces.Pop();
            }
            // Missing closing braces are routine while typing. The parser's
            // consumed-token range excludes trailing trivia, but the scope extends
            // through the caret at EOF until that brace is supplied.
            foreach (var node in nodes[file.Path].Where(n => n.Node is BlockStatement or MethodDeclaration or ConstructorDeclaration or ClassDeclaration or StructDeclaration or InterfaceDeclaration or NamespaceDeclaration or PropertyDeclaration or LambdaExpression or OperatorDeclaration or ExtensionDeclaration))
                if (braces.Any(start => start >= node.Span.Start && start < node.Span.Start + node.Span.Length))
                    scopeEnds[node] = file.Text.Length + 1;
            contexts[file.Path] = new SyntaxRangeIndex(nodes[file.Path], scopeEnds);
            foreach (var group in nodes[file.Path].Where(n => n.Node is CallExpression)
                .GroupBy(n => { var call = (CallExpression)n.Node; return call.Callee.Span.Start + call.Callee.Span.Length; }))
                callsByCalleeEnd[(file.Path, group.Key)] = group.OrderBy(n => n.Span.Length).ToArray();
            foreach (var syntax in nodes[file.Path].Where(n => n.Node is NamedTypeExpression && n.Span.Length > 0))
            {
                var fileTokens = tokens[file.Path];
                for (int index = TokenStartIndex(fileTokens, syntax.Span.Start);
                    index < fileTokens.Length && fileTokens[index].End < syntax.Span.Start + syntax.Span.Length; index++)
                    typePositionTokens.Add(fileTokens[index]);
            }
            foreach (var syntax in nodes[file.Path])
            {
                int start = syntax.Node switch {
                    Parameter { IsExtensionReceiver: true } => syntax.Span.Start,
                    PropertyDeclaration { Name: "$index" } property => property.Type.Span.Start + property.Type.Span.Length,
                    _ => -1
                };
                if (start < 0) continue;
                var fileTokens = tokens[file.Path];
                int index = TokenStartIndex(fileTokens, start);
                if (index < fileTokens.Length && fileTokens[index].Text == "this" &&
                    (syntax.Node is not Parameter || fileTokens[index].Start == start)) receiverKeywordTokens.Add(fileTokens[index]);
            }
        }
        sourceIndexTiming?.Dispose();
        var declarationIndexTiming = PipelineTimings.Measure("editor.index.declarations");
        foreach (var entry in activeNodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (name, kind, classification, type) = Describe(entry.Node);
            if (name == null) continue;
            var parents = Parents(entry).ToArray();
            if (entry.Node is ConstructorDeclaration or MethodDeclaration { StaticConstructorName: not null })
            {
                var containingType = parents.FirstOrDefault(parent => IsType(parent.Node));
                name = containingType == null ? name : SourceName(containingType.Node);
                classification = containingType?.Node is StructDeclaration ? "struct" : "class";
            }
            if (name.Contains('$')) continue;
            string ns = entry.Parent?.Node is ExtensionDeclaration ? Namespace(parents)
                : snapshot.Checker?.DeclarationNamespace(entry.Node) ?? Namespace(parents);
            string? owner = Owner(parents);
            if (owner == null && entry.Node is MethodDeclaration qualified && snapshot.Checker != null &&
                methodOwners.TryGetValue(qualified, out var methodOwner))
                owner = snapshot.Checker.DisplayTypeName(new NamedTypeExpression(methodOwner.Owner, null, qualified.Line));
            if (entry.Node is NamespaceDeclaration declaration && declaration.Name.Contains("::"))
            {
                foreach (string part in declaration.Name.Split("::"))
                {
                    symbols.Add(new(part, 9, "namespace", entry, NameSpan(entry, part, null), ns, null, null, null, false, TokenKind.Public));
                    ns = Join(ns, part);
                }
                continue;
            }
            Syntax? scope = entry.Node switch
            {
                VariableDeclaration => parents.FirstOrDefault(p => p.Node is BlockStatement or ForStatement),
                AliasDeclaration => parents.FirstOrDefault(p => p.Node is BlockStatement),
                EnumDeclaration => parents.FirstOrDefault(p => p.Node is BlockStatement),
                Parameter => parents.FirstOrDefault(p => p.Node is MethodDeclaration or ExternDeclaration or ConstructorDeclaration or LambdaExpression or OperatorDeclaration),
                ForeachStatement or StaticForeachStatement or CatchClause => entry,
                _ => null
            };
            TypeExpression? declaredType = type;
            var nameSpan = NameSpan(entry, name, declaredType);
            if (entry.Node is NamespaceDeclaration) owner = null;
            var symbol = new Symbol(name, kind, classification, entry, nameSpan, ns, owner, scope, type,
                IsStatic(entry.Node), entry.Node is MethodDeclaration && entry.Parent?.Node is InterfaceDeclaration ? TokenKind.Public : Access(entry.Node));
            symbols.Add(symbol);
            if (IsType(entry.Node) && entry.Span.Source != null)
                sourceTypeDeclarations.TryAdd((entry.Node.GetType(), entry.Span), symbol);
            if (symbol.Parameters != null && entry.Span.Source != null)
                sourceCallableDeclarations.TryAdd((entry.Node.GetType(), entry.Span), symbol);
            declarations.TryAdd(entry.Node, symbol);
        }
        using (PipelineTimings.Measure("editor.index.generic-parameters")) RegisterTypeParameters();
        foreach (var group in symbols.GroupBy(symbol => symbol.Name)) symbolsByName.Add(group.Key, group.ToArray());
        foreach (var symbol in symbols)
        {
            if (symbol.NameSpan.Source?.Path is { } path)
                declarationsBySpan.TryAdd((path, symbol.NameSpan.Start, symbol.NameSpan.Length), symbol);
            if ((symbol.IsTypeParameter || IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration) && HasSourceIdentifierEscape(symbol))
                escapedTypeDisplayNames.Add(symbol.Name);
        }
        declarationIndexTiming?.Dispose();
        using (PipelineTimings.Measure("editor.index.type-names")) IndexSourceTypeNames();
        cancellationToken.ThrowIfCancellationRequested();
        using (PipelineTimings.Measure("editor.index.type-arguments")) IndexTypeArgumentSeparators();
        using (PipelineTimings.Measure("editor.index.callables")) IndexBoundCallables();
        cancellationToken.ThrowIfCancellationRequested();
        using (PipelineTimings.Measure("editor.index.properties")) IndexBoundProperties();
        using (PipelineTimings.Measure("editor.index.fields")) IndexBoundFields();
        cancellationToken.ThrowIfCancellationRequested();
        using (PipelineTimings.Measure("editor.index.splices")) IndexSpliceReferences();
        using (PipelineTimings.Measure("editor.index.documentation")) IndexDocumentation();
    }

    private string ParameterText(Parameter p, Symbol context)
    {
        var symbol = declarations.GetValueOrDefault(p);
        if (symbol == null)
        {
            var sourceParameters = context.Syntax.Node switch
            {
                MethodDeclaration method => method.Parameters,
                ConstructorDeclaration constructor => constructor.Parameters,
                ExternDeclaration external => external.Parameters,
                OperatorDeclaration op => op.Parameters,
                _ => null
            };
            if (sourceParameters?.FirstOrDefault(parameter => parameter.Name == p.Name) is { } original)
                symbol = declarations.GetValueOrDefault(original);
        }
        string name = symbol != null ? CompletionSpelling(symbol) :
            Lexer.GetKeyword(p.Name) != TokenKind.Identifier ? "@" + p.Name : p.Name;
        return $"{(p.IsExtensionReceiver ? "this " : "")}{(p.IsConst ? "const " : "")}{SourceTypeSpelling(p.Type, context)} {name}" +
            (p.DefaultValue == null ? "" : " = " + ParameterDefaultText(p));
    }
    private static string Join(string left, string right) => left.Length == 0 ? right : left + "::" + right;
    private static IEnumerable<Syntax> Parents(Syntax node) { for (var p = node.Parent; p != null; p = p.Parent) yield return p; }
    private void IndexScopeEnvironments()
    {
        // Containers retain source parents; their member lists reflect qualified
        // binding and weak replacement selection.
        var methodScopes = new Dictionary<MethodDeclaration, (string Namespace, string? Owner, bool HasNamespace)>(ReferenceEqualityComparer.Instance);
        foreach (var container in activeNodes)
        {
            IEnumerable<AstNode>? members = container.Node switch
            {
                CompilationUnit unit => unit.Members,
                NamespaceDeclaration declaration => declaration.Members,
                ClassDeclaration cls => cls.Members,
                StructDeclaration structure => structure.Members,
                _ => null
            };
            if (members == null) continue;
            var ancestry = Parents(container).Prepend(container).ToArray();
            string containerNamespace = Namespace(ancestry);
            string? containerOwner = Owner(ancestry);
            bool containerHasNamespace = ancestry.Any(parent => parent.Node is NamespaceDeclaration);
            foreach (var method in members.OfType<MethodDeclaration>())
                methodScopes[method] = (containerNamespace, containerOwner, containerHasNamespace);
        }
        scopeEnvironments.EnsureCapacity(activeNodes.Count);
        var pending = new Stack<Syntax>();
        foreach (var syntax in activeNodes)
        {
            Syntax? current = syntax;
            ScopeEnvironment environment = new("", null, false, false);
            while (current != null)
            {
                if (scopeEnvironments.TryGetValue(current, out environment)) break;
                pending.Push(current); current = current.Parent;
            }
            if (current == null) environment = new("", null, false, false);
            while (pending.TryPop(out var entry))
            {
                string ns = environment.Namespace;
                string? owner = environment.Owner;
                bool hasNamespace = environment.HasNamespace;
                if (entry.Node is NamespaceDeclaration declaration)
                {
                    ns = hasNamespace ? ns + "::" + declaration.Name : declaration.Name;
                    if (owner != null) owner += "::" + declaration.Name;
                    hasNamespace = true;
                }
                else if (IsType(entry.Node))
                    owner = owner != null ? owner + "::" + SourceName(entry.Node)
                        : hasNamespace ? ns + "::" + SourceName(entry.Node) : SourceName(entry.Node);
                else if (entry.Parent?.Node is MethodDeclaration declaringMethod &&
                    methodScopes.TryGetValue(declaringMethod, out var boundMethodScope))
                {
                    // The method entry keeps its owner prefix lexical; its type,
                    // parameter and body children use the bound containing scope.
                    ns = boundMethodScope.Namespace;
                    owner = boundMethodScope.Owner;
                    hasNamespace = boundMethodScope.HasNamespace;
                }
                environment = new(ns, owner, hasNamespace,
                    entry.Node is MethodDeclaration method ? method.IsStatic : environment.IsStatic);
                scopeEnvironments.Add(entry, environment);
            }
        }
    }
    private static bool IsType(AstNode node) => node is ClassDeclaration or StructDeclaration or InterfaceDeclaration or EnumDeclaration;
    private static string SourceName(AstNode node) => node switch { ClassDeclaration c => c.SourceName, StructDeclaration s => s.SourceName, InterfaceDeclaration i => i.SourceName, EnumDeclaration e => e.SourceName, _ => "" };
    private static string Namespace(IEnumerable<Syntax> parents) => string.Join("::", parents.Reverse().Where(p => p.Node is NamespaceDeclaration).Select(p => ((NamespaceDeclaration)p.Node).Name));
    private static string? Owner(IEnumerable<Syntax> parents)
    {
        var ordered = parents.Reverse().ToArray();
        if (!ordered.Any(p => IsType(p.Node))) return null;
        return string.Join("::", ordered.Where(p => IsType(p.Node) || p.Node is NamespaceDeclaration).Select(p => p.Node is NamespaceDeclaration n ? n.Name : SourceName(p.Node)));
    }
    private static bool IsStatic(AstNode n) => n switch { MethodDeclaration m => m.IsStatic, FieldDeclaration f => f.IsStatic || f.IsConst, PropertyDeclaration p => p.Getter?.IsStatic ?? p.Setter?.IsStatic ?? false, EnumMemberDeclaration => true, _ => false };
    private static TokenKind Access(AstNode n) => n switch { MethodDeclaration m => m.Accessibility, ExternDeclaration e => e.Accessibility, FieldDeclaration f => f.Accessibility, PropertyDeclaration p => PropertyAccess(p), ClassDeclaration c => c.Accessibility, StructDeclaration s => s.Accessibility, InterfaceDeclaration i => i.Accessibility, EnumDeclaration e => e.Accessibility, ConstructorDeclaration c => c.Accessibility, _ => TokenKind.Public };
    private static TokenKind PropertyAccess(PropertyDeclaration property)
        => new[] { property.Getter, property.Setter }.OfType<MethodDeclaration>()
            .Select(m => m.Accessibility).OrderByDescending(a => a == TokenKind.Public ? 2 : a == TokenKind.Private ? 0 : 1)
            .FirstOrDefault(TokenKind.Private);
    private static (string? Name, int Kind, string Classification, TypeExpression? Type) Describe(AstNode node) => node switch
    {
        ClassDeclaration c => (c.SourceName, 7, "class", null),
        StructDeclaration s => (s.SourceName, 22, "struct", null),
        InterfaceDeclaration i => (i.SourceName, 8, "interface", null),
        EnumDeclaration e => (e.SourceName, 13, "enum", null),
        NamespaceDeclaration n => (n.Name, 9, "namespace", null),
        MethodDeclaration { StaticConstructorName: not null } m => (m.StaticConstructorName, 4, "method", null),
        MethodDeclaration m when m.PropertyName == null => (m.Name, 2, "method", m.ReturnType),
        ExternDeclaration e => (e.Name, 3, "function", e.ReturnType),
        ConstructorDeclaration c => (c.Name.Split("::").Last(), 4, "method", null),
        FieldDeclaration f => (f.Name, f.IsConst ? 21 : 5, f.IsConst ? "constant" : "field", f.Type),
        PropertyDeclaration p => (p.Name, 10, "property", p.Type),
        VariableDeclaration v => (v.Name, v.IsConst ? 21 : 6, v.IsConst ? "constant" : "variable", v.Type),
        Parameter p => (p.Name, 6, "parameter", p.Type),
        EnumMemberDeclaration e => (e.Name, 20, "enumMember", null),
        AliasDeclaration a => (a.Name, 7, "type", a.TargetType),
        ForeachStatement f => (f.VariableName, 6, "variable", f.ElementType),
        StaticForeachStatement f => (f.VariableName, 6, "variable", f.ElementType),
        CatchClause c => (c.VariableName, 6, "variable", c.ExceptionType),
        _ => (null, 0, "", null)
    };

    private SourceSpan NameSpan(Syntax node, string name, TypeExpression? type)
    {
        var span = node.Span;
        if (span.Source == null || !tokens.TryGetValue(span.Source.Path, out var fileTokens)) return span with { Length = 0 };
        Token? first = null, firstIdentifier = null, keyword = null;
        // In `String String`, the first occurrence belongs to the type.
        for (int index = TokenStartIndex(fileTokens, span.Start);
            index < fileTokens.Length && fileTokens[index].End < span.Start + span.Length; index++)
        {
            var candidate = fileTokens[index];
            if (candidate.Text != name) continue;
            first ??= candidate;
            if (candidate.Kind == TokenKind.Identifier) firstIdentifier ??= candidate;
            if (type == null || type.Span.Source != span.Source || candidate.Start >= type.Span.Start + type.Span.Length)
            {
                // In `class @class`, the keyword and name have equal text.
                // Operators and indexers deliberately use keyword name spans.
                if (candidate.Kind == TokenKind.Identifier || node.Node is OperatorDeclaration or PropertyDeclaration { Name: "$index" })
                    return candidate.Span;
                keyword ??= candidate;
            }
        }
        return firstIdentifier?.Span ?? keyword?.Span ?? first?.Span ?? span with { Length = 0 };
    }

    private bool Contains(Syntax node, int offset) => offset >= node.Span.Start && offset < scopeEnds.GetValueOrDefault(node, node.Span.Start + node.Span.Length);
    private Syntax? Context(string path, int offset) => contexts.GetValueOrDefault(path)?.Find(offset);
    private ScopeEnvironment ScopeAt(string path, int offset)
    {
        var context = Context(path, offset);
        return context == null ? new ScopeEnvironment("", null, false, false) : scopeEnvironments[context];
    }
    private Symbol? SourceOwnerType(string owner)
    {
        int separator = owner.LastIndexOf("::", StringComparison.Ordinal);
        string name = separator < 0 ? owner : owner[(separator + 2)..];
        return SymbolsNamed(name).FirstOrDefault(symbol => !symbol.IsTypeParameter && IsType(symbol.Syntax.Node) && symbol.Qualified == owner);
    }

    private IEnumerable<Symbol> EnclosingTypeDeclarations(string owner, string? name)
    {
        if (SourceOwnerType(owner) is not { } current) yield break;
        foreach (var ancestor in Parents(current.Syntax))
        {
            if (!IsType(ancestor.Node) || !declarations.TryGetValue(ancestor.Node, out var containing)) continue;
            foreach (var candidate in TypeMembers(containing.Qualified))
                if (IsType(candidate.Syntax.Node) && candidate.Owner == containing.Qualified &&
                    (name == null || candidate.Name == name)) yield return candidate;
        }
    }

    private bool NestedPrivateAccess(string accessor, string declaring)
    {
        var first = SourceOwnerType(accessor);
        var second = SourceOwnerType(declaring);
        return first != null && second != null &&
            (Parents(first.Syntax).Any(parent => ReferenceEquals(parent.Node, second.Syntax.Node)) ||
             Parents(second.Syntax).Any(parent => ReferenceEquals(parent.Node, first.Syntax.Node)));
    }

    private IEnumerable<Symbol> Visible(string path, int offset, string? name = null)
    {
        var environment = ScopeAt(path, offset);
        string ns = environment.Namespace;
        string? owner = environment.Owner;
        bool isStatic = environment.IsStatic;
        var imports = snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? [];
        IEnumerable<Symbol> memberOrder = owner == null ? [] : TypeMembers(owner);
        var inheritedOrder = owner == null ? Array.Empty<Symbol>() : memberOrder
            .Where(s => !IsType(s.Syntax.Node) || s.Owner == owner)
            .Concat(EnclosingTypeDeclarations(owner, name))
            .Concat(memberOrder.Where(s => IsType(s.Syntax.Node) && s.Owner != owner))
            .Where(s => (name == null || s.Name == name) && Accessible(s, path, offset)).ToArray();
        var inherited = inheritedOrder.ToHashSet();
        return SymbolsNamed(name).Where(s => s.Scope != null
            ? s.NameSpan.Source?.Path == path && Contains(s.Scope, offset) && (s.IsTypeParameter || s.Syntax.Node is Parameter || s.NameSpan.Start <= offset)
            : s.Owner != null ? inherited.Contains(s) && (!isStatic || s.Static || IsType(s.Syntax.Node))
            : s.Namespace.Length == 0 || ns == s.Namespace || ns.StartsWith(s.Namespace + "::", StringComparison.Ordinal) || imports.Contains(s.Namespace))
            .Where(s => s.IsTypeParameter || s.Syntax.Node switch
            {
                // Owning-type members were checked in inheritedOrder above.
                MethodDeclaration when s.Syntax.Parent?.Node is ExtensionDeclaration => false,
                MethodDeclaration => s.Owner != null || Accessible(s, path, offset),
                ExternDeclaration or PropertyDeclaration or FieldDeclaration => Accessible(s, path, offset),
                _ => true
            })
            .OrderBy(s => s.Scope == null ? int.MaxValue : s.Scope.Span.Length)
            .ThenBy(s => inherited.Contains(s) ? Array.IndexOf(inheritedOrder, s) : int.MaxValue)
            .ThenByDescending(s => s.NameSpan.Start);
    }

    private IEnumerable<Symbol> SymbolsNamed(string? name) => name == null ? symbols : symbolsByName.GetValueOrDefault(name) ?? [];

    private IEnumerable<Symbol> Members(string path, int offset, int separatorIndex, string? name = null, Token? insertedSeparator = null)
    {
        var ts = tokens[path];
        if (separatorIndex < 1) return [];
        var separator = insertedSeparator ?? ts[separatorIndex];
        var left = ts[separatorIndex - 1];
        if (left.Text == "this" && ScopeAt(path, offset).IsStatic &&
            !ContextParents(path, offset).Any(parent => parent.Node is TypeofExpression)) return [];
        if (separator.Kind == TokenKind.DoubleColon)
        {
            if (left.Kind is TokenKind.Greater or TokenKind.GreaterGreater)
            {
                var access = nodes[path].Select(node => node.Node).OfType<NamespaceAccessExpression>()
                    .FirstOrDefault(access => access.Left is TypeExpression type &&
                        type.Span.Start + type.Span.Length == left.End + 1 &&
                        separatorIndex + 1 < ts.Length && access.Member == ts[separatorIndex + 1].Text &&
                        access.Span.Start <= separator.Start && access.Span.Start + access.Span.Length >= ts[separatorIndex + 1].End + 1);
                return access?.Left is TypeExpression qualifierType
                    ? SourceGenericStaticMethods(qualifierType, path, offset, name) : [];
            }
            int start = separatorIndex - 1;
            while (start >= 2 && ts[start - 1].Kind == TokenKind.DoubleColon) start -= 2;
            string qualifier = string.Concat(ts[start..separatorIndex].Select(t => t.Text));
            bool globalQualifier = qualifier.StartsWith("global::");
            if (globalQualifier) qualifier = qualifier[8..];
            if (qualifier == "global") qualifier = "";
            var resolved = qualifier.Length == 0 ? null
                : Candidates(path, offset, separatorIndex - 1, left.Text).FirstOrDefault(s => s.Name == left.Text &&
                    (s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration))
                ?? symbols.FirstOrDefault(s => s.Syntax.Node is AliasDeclaration && s.Qualified == qualifier);
            string head = qualifier.Split("::")[0];
            bool typePrefix = resolved != null || Visible(path, offset, head).Any(s =>
                s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
            string? namespacePath = typePrefix ? null : snapshot.Checker?.ResolveVisibleNamespacePath(qualifier,
                globalQualifier ? "" : ScopeAt(path, offset).Namespace,
                globalQualifier ? [] : snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? []);
            if (resolved == null && snapshot.Checker == null)
                resolved = Visible(path, offset, qualifier).FirstOrDefault(s => s.Name == qualifier && s.Classification == "namespace");
            if (resolved is { IsTypeParameter: true, TypeConstraint: null }) return [];
            if (resolved != null && ContractOwners(resolved, path, offset) is { } owners)
            {
                return SymbolsNamed(name).Where(s => owners.Contains(s.Owner) && s.Static && Accessible(s, path, offset));
            }
            qualifier = namespacePath ?? (resolved is { IsTypeParameter: true }
                ? TypeOwner(resolved.TypeConstraint, path, offset) ?? qualifier
                : resolved?.Syntax.Node is AliasDeclaration
                    ? TypeOwner(resolved.Type, resolved.NameSpan.Source?.Path ?? path, resolved.NameSpan.Start) ?? qualifier
                    : resolved?.Qualified ?? qualifier);
            return SymbolsNamed(name).Where(s => s.Scope == null && s.Syntax.Parent?.Node is not ExtensionDeclaration &&
                (s.Namespace == qualifier && s.Owner == null || s.Owner == qualifier && (s.Static || IsType(s.Syntax.Node))) && Accessible(s, path, offset));
        }
        var expression = nodes[path].Where(n => n.Node is IdentifierExpression or CallExpression or MemberAccessExpression or NewExpression or IndexExpression or UnaryExpression or CastExpression or TypeofExpression or ReflectExpression or SpliceExpression or LiteralExpression or PrefixedStringLiteralExpression or InterpolatedStringExpression && n.Span.Start + n.Span.Length == left.End + 1)
            .OrderByDescending(n => n.Span.Length).FirstOrDefault();
        TypeExpression? type = expression == null ? null : ExpressionType(expression.Node, path, offset);
        if (type == null)
            type = Visible(path, offset, left.Text).FirstOrDefault(s => s.Name == left.Text)?.Type;
        string? owner = TypeOwner(type, path, offset);
        if (left.Text == "this" && ScopeAt(path, offset).Owner is { } thisOwner) owner = thisOwner;
        var members = owner == null ? [] : TypeMembers(owner).Where(s => (name == null || s.Name == name) && !s.Static && !IsType(s.Syntax.Node) && Accessible(s, path, offset) && ReceiverAllowsMember(s, type))
            .Select(member => ProjectSourceMemberType(BindInstanceMember(member, type), type, path, offset)).ToArray();
        return members.Concat(ExtensionMembers(path, offset, expression?.Node, type, name)
            .Where(extension => name == null || extension.Name == name)
            .Where(extension => !members.Any(member => member.Name == extension.Name && IsCallableMember(member))));
    }

    private static bool ReceiverAllowsMember(Symbol symbol, TypeExpression? receiver)
    {
        while (receiver is ReferenceTypeExpression reference) receiver = reference.Inner;
        receiver = receiver switch { PointerTypeExpression pointer => pointer.Inner, ManagedTypeExpression managed => managed.Inner, _ => receiver };
        var method = symbol.Syntax.Node switch { MethodDeclaration callable => callable, PropertyDeclaration property => property.Getter, _ => null };
        if (method == null || receiver == null) return true;
        if (receiver.IsImmutableValue) return method.IsImmutable;
        return !receiver.IsReadOnlyValue || method.IsReadOnly;
    }

    private HashSet<string?>? ContractOwners(Symbol alias, string path, int offset)
    {
        var seen = new HashSet<Symbol>();
        while (alias.Syntax.Node is AliasDeclaration declaration && seen.Add(alias))
        {
            if (declaration.ImplementationKind == FunctionImplementationKind.Replace &&
                symbols.FirstOrDefault(s => s.Syntax.Node is AliasDeclaration { ImplementationKind: not FunctionImplementationKind.Replace } &&
                    s.Qualified == alias.Qualified) is { } original)
            {
                alias = original;
                continue;
            }
            string declaringPath = alias.NameSpan.Source?.Path ?? path;
            int declaringOffset = alias.NameSpan.Start;
            if (declaration.Constraints.Count > 0)
                return declaration.Constraints.Select(c => TypeOwner(c, declaringPath, declaringOffset)).ToHashSet();
            string? targetName = declaration.TargetType switch {
                NamedTypeExpression n => n.Namespace == null ? n.Name : Join(n.Namespace, n.Name),
                NestedTypeExpression n => DisplayType(n), _ => null
            };
            if (targetName == null) break;
            var next = Visible(declaringPath, declaringOffset, targetName).FirstOrDefault(s => s.Name == targetName && s.Syntax.Node is AliasDeclaration)
                ?? symbols.FirstOrDefault(s => s.Syntax.Node is AliasDeclaration && s.Qualified == targetName);
            if (next == null) break;
            alias = next;
        }
        return null;
    }

    private TypeExpression? ExpressionType(AstNode node, string path, int offset)
    {
        var checkedType = snapshot.Checker?.GetType(node);
        if (checkedType != null && TypeChecker.TypeName(checkedType) != "<error>") return checkedType;
        // Incomplete access often prevents checking. Recover only unambiguous
        // declaration types; never guess between overloads with different returns.
        if (node is IdentifierExpression { Name: "this" } &&
            !ContextParents(path, offset).Any(parent => parent.Node is TypeofExpression) &&
            ContextParents(path, offset).Any(parent => parent.Node is MethodDeclaration { IsStatic: true })) return null;
        if (node is IdentifierExpression { Name: "this" } &&
            ContextParents(path, offset).FirstOrDefault(parent => parent.Node is ExtensionDeclaration)?.Node is ExtensionDeclaration block)
        {
            var method = ContextParents(path, offset).Select(parent => parent.Node).OfType<MethodDeclaration>().FirstOrDefault();
            var target = method?.IsImmutable == true ? TypeQualifiers.Immutable(block.Target) : block.Target;
            return new PointerTypeExpression(target, false, node.Line, method?.IsReadOnly == true);
        }
        if (node is IdentifierExpression { Name: "this" } &&
            ContextParents(path, offset).FirstOrDefault(parent => IsType(parent.Node)) is { } enclosingType)
        {
            TypeExpression? type = null;
            foreach (var container in Parents(enclosingType).Reverse().Where(parent => IsType(parent.Node)).Append(enclosingType))
            {
                var parameters = container.Node switch { ClassDeclaration c => c.GenericParameters, StructDeclaration s => s.GenericParameters, _ => null };
                var arguments = parameters?.Select(p => (TypeExpression)new NamedTypeExpression(p.Name, null, node.Line)).ToList();
                type = type == null ? new NamedTypeExpression(Join(Namespace(Parents(container)), SourceName(container.Node)), null, node.Line, arguments)
                    : new NestedTypeExpression(type, SourceName(container.Node), arguments, node.Line);
            }
            var method = ContextParents(path, offset).Select(parent => parent.Node).OfType<MethodDeclaration>().FirstOrDefault();
            return new PointerTypeExpression(method?.IsImmutable == true ? TypeQualifiers.Immutable(type!) : type!, false, node.Line, method?.IsReadOnly == true);
        }
        if (node is IdentifierExpression identifier) return Visible(path, offset, identifier.Name).FirstOrDefault(s => s.Name == identifier.Name)?.Type;
        if (node is NewExpression creation) return creation.Type;
        if (node is CastExpression cast) return cast.TargetType;
        if (node is TypeofExpression or ReflectExpression) return TypeChecker.ReflectionType(node.Line);
        if (node is SpliceExpression { Resolved: not null } splice) return ExpressionType(splice.Resolved, path, offset);
        if (node is UnaryExpression { Operator: TokenKind.Bang, IsPrefix: false } suppression)
            return ExpressionType(suppression.Operand, path, offset);
        if (node is UnaryExpression { Operator: TokenKind.Star } dereference)
        {
            var operand = ExpressionType(dereference.Operand, path, offset);
            return operand is PointerTypeExpression pointer ? pointer.Inner : null;
        }
        if (node is IndexExpression { Indices.Count: 1 } indexed)
        {
            var receiver = ExpressionType(indexed.Target, path, offset);
            return BuiltinIndexedElementType(receiver, path, offset) ?? SourceIndexerElementType(indexed, receiver, path, offset);
        }
        if (node is MemberAccessExpression member)
        {
            var receiver = ExpressionType(member.Object, path, offset);
            if (receiver != null && TypeChecker.IsReflectionType(receiver)) return TypeChecker.ReflectionMemberType(member.Member, member.Line);
            string? owner = TypeOwner(receiver, path, offset);
            var source = owner == null ? null : TypeMembers(owner).FirstOrDefault(s => s.Name == member.Member);
            return source == null ? null : ProjectSourceMemberType(source, receiver, path, offset).Type;
        }
        if (node is CallExpression call)
        {
            var ts = tokens[path];
            int index = Array.FindLastIndex(ts, t => t.Kind == TokenKind.Identifier && t.End + 1 == call.Callee.Span.Start + call.Callee.Span.Length);
            if (index >= 0)
            {
                // A source method's unbound generic return is display metadata,
                // not an inferred result type in the caller's lexical scope.
                var returns = Candidates(path, offset, index).Where(s => s.Name == ts[index].Text && s.Parameters != null)
                    .Where(s => s.Type == null || s.Syntax.Node is not MethodDeclaration method || method.GenericParameters.Count == 0 ||
                        !HasUnboundDisplayParameter(s.Type, method.GenericParameters.Select(parameter => parameter.Name).ToHashSet()))
                    .Select(s => s.Type).OfType<TypeExpression>().DistinctBy(TypeChecker.TypeName).ToArray();
                if (returns.Length == 1) return returns[0];
            }
        }
        return null;
    }
    private TypeExpression? BuiltinIndexedElementType(TypeExpression? type, string path, int offset)
    {
        var aliases = new HashSet<Symbol>();
        while (type != null)
        {
            if (type is ReferenceTypeExpression reference) { type = reference.Inner; continue; }
            if (type is PointerTypeExpression pointer) return pointer.Inner;
            if (type is ArrayTypeExpression array) return array.ElementType;
            if (type is not NamedTypeExpression named) return null;
            string name = named.Namespace == null ? named.Name : Join(named.Namespace, named.Name);
            var lexical = Visible(path, offset, name).FirstOrDefault(symbol => symbol.Name == name &&
                (symbol.IsTypeParameter || IsType(symbol.Syntax.Node) || symbol.Syntax.Node is AliasDeclaration));
            var alias = lexical?.Syntax.Node is AliasDeclaration ? lexical : lexical == null
                ? symbols.FirstOrDefault(symbol => symbol.Scope == null && symbol.Syntax.Node is AliasDeclaration && symbol.Qualified == name) : null;
            if (alias == null || !aliases.Add(alias)) return null;
            path = alias.NameSpan.Source?.Path ?? path;
            offset = alias.NameSpan.Start;
            type = alias.Type?.WithQualifiersFrom(named);
        }
        return null;
    }

    private IEnumerable<Syntax> ContextParents(string path, int offset)
    {
        var context = Context(path, offset);
        return context == null ? [] : new[] { context }.Concat(Parents(context));
    }
    private bool Accessible(Symbol s, string path, int offset)
    {
        if ((s.Syntax.Node is ExternDeclaration || s.Syntax.Node is MethodDeclaration or PropertyDeclaration or FieldDeclaration && s.Owner == null) && s.Access == TokenKind.Private)
            return Workspace.Paths.Equals(s.Syntax.Span.Source?.Path, path);
        if (s.Access is TokenKind.Public or TokenKind.Internal || s.Owner == null) return true;
        string? owner = ScopeAt(path, offset).Owner;
        return s.Owner == owner ||
            s.Access == TokenKind.Private && owner != null && NestedPrivateAccess(owner, s.Owner) ||
            s.Access == TokenKind.Protected && owner != null && TypeMembers(owner).Any(m => m.Owner == s.Owner);
    }
    private static string TemplateName(string name)
    {
        var result = new System.Text.StringBuilder();
        int depth = 0;
        foreach (char c in name)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (depth == 0) result.Append(c);
        }
        return result.ToString();
    }
    private string? TypeOwner(TypeExpression? type, string path, int offset)
    {
        var aliases = new HashSet<Symbol>();
        while (true)
        {
            while (type is PointerTypeExpression or ManagedTypeExpression or ReferenceTypeExpression)
                type = type switch { PointerTypeExpression p => p.Inner, ManagedTypeExpression m => m.Inner, ReferenceTypeExpression r => r.Inner, _ => type };
            if (type is NestedTypeExpression nested)
            {
                string? parent = TypeOwner(nested.Parent, path, offset) ?? SourceTypeNamespace(nested.Parent, path, offset);
                if (parent == null) return null;
                string qualified = Join(parent, nested.Member);
                var target = symbols.FirstOrDefault(s => !s.IsTypeParameter && s.Qualified == qualified);
                if (target?.Syntax.Node is AliasDeclaration)
                {
                    if (!aliases.Add(target)) return null;
                    path = target.NameSpan.Source?.Path ?? path;
                    offset = target.NameSpan.Start;
                    type = target.Type;
                    continue;
                }
                return target != null && IsType(target.Syntax.Node) ? target.Qualified : null;
            }
            if (type is not NamedTypeExpression named) return null;
            string name = named.Namespace == null ? named.Name : Join(named.Namespace, named.Name);
            var lexical = Visible(path, offset, name).FirstOrDefault(s => s.Name == name &&
                (s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration));
            var parameter = lexical?.IsTypeParameter == true ? lexical : null;
            if (parameter != null)
            {
                if (parameter.TypeConstraint == null || !aliases.Add(parameter)) return null;
                type = parameter.TypeConstraint;
                continue;
            }
            var alias = lexical?.Syntax.Node is AliasDeclaration ? lexical : lexical == null
                ? symbols.FirstOrDefault(s => s.Scope == null && s.Syntax.Node is AliasDeclaration && s.Qualified == name) : null;
            if (alias != null)
            {
                if (!aliases.Add(alias)) return null;
                path = alias.NameSpan.Source?.Path ?? path;
                offset = alias.NameSpan.Start;
                type = alias.Type;
                continue;
            }
            // Specialized declarations retain their original source spans. Use
            // their registered origin before falling back to incomplete-source
            // name recovery, which cannot distinguish a nested specialization
            // from its generic containing type.
            var known = snapshot.Checker?.GetSourceTypeBinding(named) as NamedTypeExpression;
            if (known == null && snapshot.Checker?.IsResolvedTypeIdentity(named) != true &&
                sourceTypeNames.TryGetValue(named.NameSpan, out var origin))
                known = snapshot.Checker?.GetSourceTypeBinding(origin) as NamedTypeExpression;
            if (known == null && snapshot.Checker?.IsResolvedTypeIdentity(named) != true &&
                lexical != null && IsType(lexical.Syntax.Node)) return lexical.Qualified;
            var nominal = known ?? named;
            if (snapshot.Checker?.GetTypeDeclaration(nominal.Name) is { } declaration &&
                sourceTypeDeclarations.TryGetValue((declaration.GetType(), declaration.Span), out var sourceType))
                return sourceType.Qualified;
            // Source declarations retain their internal names after checking. Match
            // those before decoding a specialization, even when checking is incomplete.
            var internalMatch = symbols.Where(s => !s.IsTypeParameter && IsType(s.Syntax.Node)).Select(s => (Symbol: s, Name: s.Syntax.Node switch {
                ClassDeclaration c => c.Name, StructDeclaration st => st.Name, InterfaceDeclaration i => i.Name, EnumDeclaration e => e.Name, _ => "" }))
                .Where(s => named.Name == s.Name || named.Name.StartsWith(s.Name + "$", StringComparison.Ordinal))
                .OrderByDescending(s => s.Name.Length).FirstOrDefault();
            if (internalMatch.Symbol != null && (named.Namespace == null || named.Namespace == internalMatch.Symbol.Namespace)) return internalMatch.Symbol.Qualified;
            string display = TemplateName(DisplayType(named.WithoutValueProtection()));
            return symbols.FirstOrDefault(s => !s.IsTypeParameter && IsType(s.Syntax.Node) && (s.Qualified == name || s.Qualified == display))?.Qualified
                ?? Visible(path, offset, name).FirstOrDefault(s => IsType(s.Syntax.Node) && s.Name == name)?.Qualified;
        }
    }
    private IEnumerable<Symbol> TypeMembers(string owner) => typeMemberFamilies.GetOrAdd(owner,
        static (name, model) => model.EnumerateTypeMembers(name).ToArray(), this);

    // Declaration order and inheritance are fixed for the snapshot. Accessibility
    // remains a use-site check, so cached members do not leak private/protected names.
    private IEnumerable<Symbol> EnumerateTypeMembers(string owner)
    {
        var visited = new HashSet<string>();
        while (visited.Add(owner))
        {
            foreach (var symbol in symbols.Where(s => s.Owner == owner && s.Scope == null &&
                !Parents(s.Syntax).Any(p => p.Node is PrimitiveImplementationDeclaration) &&
                s.Syntax.Node is not ConstructorDeclaration and not MethodDeclaration { StaticConstructorName: not null })) yield return symbol;
            var type = symbols.FirstOrDefault(s => s.Syntax.Node is ClassDeclaration && s.Qualified == owner);
            if (type?.Syntax.Node is not ClassDeclaration c) yield break;
            // The parser records the colon list in Interfaces; checking separates
            // its base class into ClassInfo without rewriting the declaration.
            string? baseClass = snapshot.Checker?.GetClassByIdentity(c.Name)?.BaseClass ?? c.BaseClass ??
                c.Interfaces.FirstOrDefault(name => symbols.Any(s => s.Syntax.Node is ClassDeclaration && (s.Qualified == name || s.Qualified == Join(type.Namespace, name))));
            if (baseClass == null) yield break;
            owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) &&
                (s.Syntax.Node is ClassDeclaration baseDeclaration && baseDeclaration.Name == baseClass ||
                 s.Qualified == baseClass || s.Qualified == Join(type.Namespace, baseClass)))?.Qualified ?? baseClass;
        }
    }
    private IEnumerable<Symbol> Candidates(string path, int offset, int tokenIndex, string? name = null)
    {
        var ts = tokens[path];
        int previous = tokenIndex - 1;
        if (previous >= 0 && ts[previous].Kind is TokenKind.Dot or TokenKind.DoubleColon)
            return Members(path, offset, previous, name);
        return Visible(path, offset, name);
    }
    private Symbol? Resolve(string path, int index)
    {
        var token = tokens[path][index];
        if (token.Kind is not (TokenKind.Identifier or TokenKind.New)) return null;
        if (references.TryGetValue(token, out var resolved)) return resolved;
        resolved = ResolveCore(token.Span.Source?.Path ?? path, index);
        references.TryAdd(token, resolved);
        return resolved;
    }

    private void EnsureReferences()
    {
        foreach (var (path, fileTokens) in tokens)
            for (int index = 0; index < fileTokens.Length; index++)
                if (fileTokens[index].Kind is TokenKind.Identifier or TokenKind.New) Resolve(path, index);
    }

    private bool IsValueModelKeyword(string path, Token token)
    {
        if (token.Text == "this") return receiverKeywordTokens.Contains(token);
        if (token.Text is not ("move" or "copy" or "finalizer" or "at" or "ref" or "with" or "var" or "implementation" or "extension" or "explicit" or "constrained")) return false;
        using var timing = PipelineTimings.Detailed ? PipelineTimings.Measure("editor.value-keyword", token.Text) : null;
        foreach (var syntax in nodes[path])
        {
            if (token.Text == "constrained" && syntax.Node is AliasDeclaration { IsConstrained: true } &&
                token.Start >= syntax.Span.Start &&
                tokens[path].FirstOrDefault(t => t.Kind == TokenKind.Alias && t.Start >= syntax.Span.Start)?.Start is { } aliasStart &&
                token.End < aliasStart) return true;
            if (token.Text == "implementation" && syntax.Node is PrimitiveImplementationDeclaration && token.Start == syntax.Span.Start) return true;
            if (token.Text == "extension" && syntax.Node is ExtensionDeclaration && token.Start == syntax.Span.Start) return true;
            // The opt-in prefix is consumed before the implementation node's
            // located span. Recognize only its immediately following declaration.
            if (token.Text == "extension" && syntax.Node is PrimitiveImplementationDeclaration { ExposesExtensions: true } &&
                tokens[path].FirstOrDefault(next => next.Start > token.Start) is { Text: "implementation" } implementation &&
                implementation.Start == syntax.Span.Start) return true;
            if (token.Text == "explicit" && syntax.Node is MethodDeclaration { IsExplicitConversion: true } conversion &&
                token.Start >= syntax.Span.Start && token.End < conversion.ReturnType.Span.Start) return true;
            if (token.Text == "var" && syntax.Node is VariableDeclaration { IsTypeInferred: true } &&
                tokens[path].FirstOrDefault(t => t.Start >= syntax.Span.Start && t.Text == "var")?.Start == token.Start) return true;
            if (token.Start < syntax.Span.Start || token.End >= syntax.Span.Start + syntax.Span.Length) continue;
            if (token.Text == "with" && syntax.Node is NewExpression { Collector: not null } creation &&
                tokens[path].LastOrDefault(t => t.End < creation.Collector.Span.Start)?.Start == token.Start) return true;
            if (syntax.Node is MoveExpression move)
            {
                if (token.Text == "move" && token.Start == syntax.Span.Start) return true;
                if (token.Text == "at" && move.Destination != null && token.Start >= move.Operand.Span.Start + move.Operand.Span.Length && token.End < move.Destination.Span.Start) return true;
            }
            if (token.Text == "ref" && (syntax.Node is ReferenceTypeExpression || syntax.Node is UnaryExpression { Operator: TokenKind.Ref }) && token.Start == syntax.Span.Start) return true;
            if (token.Text == "ref" && syntax.Node is PropertyDeclaration { IsRef: true } property && token.End < property.Type.Span.Start) return true;
            if (token.Text == "copy" && syntax.Node is ConstructorDeclaration { IsCopy: true } &&
                !tokens[path].Any(t => t.Kind == TokenKind.OpenParen && t.Start >= syntax.Span.Start && t.Start < token.Start)) return true;
            if (token.Text == "move" && syntax.Node is ConstructorDeclaration { IsMove: true } &&
                !tokens[path].Any(t => t.Kind == TokenKind.OpenParen && t.Start >= syntax.Span.Start && t.Start < token.Start)) return true;
            if (token.Text == "finalizer" && syntax.Node is DestructorDeclaration { IsFinalizer: true } &&
                !tokens[path].Any(t => t.Kind == TokenKind.Tilde && t.Start >= syntax.Span.Start && t.Start < token.Start)) return true;
        }
        return false;
    }

    private Symbol? ResolveCore(string path, int index)
    {
        using var timing = PipelineTimings.Detailed ? PipelineTimings.Measure("editor.resolve") : null;
        var token = tokens[path][index];
        if (IsValueModelKeyword(path, token)) return null;
        if (token.Text == "at" && nodes[path].Any(n => n.Node is NewExpression placement &&
            placement.PlacementKeywordSpan.Length > 0 && placement.PlacementKeywordSpan.Start == token.Start)) return null;
        var declaration = declarationsBySpan.GetValueOrDefault((path, token.Start, token.Span.Length));
        if (declaration != null) return declaration;
        if (reflectedDeclarationReferences.TryGetValue(token, out var reflectedDeclaration)) return reflectedDeclaration;
        if (spliceReferences.TryGetValue(token, out var spliced)) return spliced;
        if (boundPropertyReferences.TryGetValue(token, out var boundProperty)) return boundProperty;
        if (boundFieldReferences.TryGetValue(token, out var boundField)) return boundField;
        if (boundCallableReferences.TryGetValue(token, out var boundCallable)) return boundCallable;
        var call = callsByCalleeEnd.GetValueOrDefault((path, token.End + 1))?
            .FirstOrDefault(n => ((CallExpression)n.Node).Callee.Span.Start <= token.Start);
        if (call?.Node is CallExpression expression && ExtensionCallSymbol(expression) is { } extension) return extension;
        if (call?.Node is CallExpression resolvedCall && snapshot.Checker?.GetResolvedCall(resolvedCall) is AstNode target && declarations.TryGetValue(target, out var method)) return method;
        // A qualifier in a named type is a namespace/type, not the final type
        // token. Resolve it before filtering type annotations to type symbols.
        if (index + 1 < tokens[path].Length && tokens[path][index + 1].Kind == TokenKind.DoubleColon)
        {
            int first = index;
            while (first >= 2 && tokens[path][first - 1].Kind == TokenKind.DoubleColon) first -= 2;
            string qualified = string.Concat(tokens[path][first..(index + 1)].Select(t => t.Text));
            bool globalQualifier = qualified.StartsWith("global::", StringComparison.Ordinal);
            if (globalQualifier) qualified = qualified[8..];
            // Match member lookup: visible types and aliases take precedence
            // over namespaces with the same spelling.
            var typeQualifier = globalQualifier
                ? symbols.FirstOrDefault(s => s.Scope == null && s.Qualified == qualified &&
                    (IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration))
                : Candidates(path, token.Start, index, token.Text).FirstOrDefault(s =>
                    s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
            if (typeQualifier != null) return typeQualifier;
            string head = qualified.Split("::")[0];
            bool typePrefix = !globalQualifier && Visible(path, token.Start, head).Any(s =>
                s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
            string? namespacePath = typePrefix ? null : snapshot.Checker?.ResolveVisibleNamespacePath(qualified,
                globalQualifier ? "" : ScopeAt(path, token.Start).Namespace,
                globalQualifier ? [] : snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? []);
            var qualifier = typePrefix ? null : symbols.FirstOrDefault(s => s.Syntax.Node is NamespaceDeclaration &&
                s.Qualified == (namespacePath ?? qualified));
            if (qualifier != null) return qualifier;
        }
        var candidates = Candidates(path, token.Start, index, token.Text);
        if (BoundTypeName(token, candidates) is { } boundType) return boundType;
        // A local named `String` must not recolor a `String` type annotation.
        bool typePosition = sourceTypeNames.ContainsKey(token.Span) || typePositionTokens.Contains(token);
        if (typePosition)
            candidates = candidates.Where(s => s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
        else if (candidates.FirstOrDefault(s => !s.IsTypeParameter && s.Name == token.Text) is { } value)
            return value;
        return candidates.FirstOrDefault(s => s.Name == token.Text);
    }
    private int TokenAt(string path, int offset)
    {
        var ts = tokens[path];
        int index = TokenStartIndex(ts, offset);
        if (index < ts.Length && ts[index].Start == offset) return index;
        return index > 0 && ts[index - 1].End >= offset ? index - 1 : -1;
    }

    public object Completion(string path, int offset) => Completion(path, offset, null);

    private static bool HasSourceIdentifierEscape(Symbol symbol)
    {
        var span = symbol.NameSpan;
        return span.Length > 0 && span.Source is { } source && span.Start > 0 &&
            span.Start <= source.Text.Length && source.Text[span.Start - 1] == '@';
    }
    private static string CompletionSpelling(Symbol symbol, bool escapedPrefix = false)
        => escapedPrefix || HasSourceIdentifierEscape(symbol) || Lexer.GetKeyword(symbol.Name) != TokenKind.Identifier ? "@" + symbol.Name : symbol.Name;

    private object Completion(string path, int offset, TextRange? replacementRange)
    {
        if (IncludeCompletion(path, offset) is { } include) return include;
        var ts = tokens[path];
        if (InCommentOrLiteral(path, offset)) return new { isIncomplete = false, items = Array.Empty<object>() };
        int index = TokenStartIndex(ts, offset);
        int start = offset;
        static bool IsWord(Token token) => token.Kind == TokenKind.Identifier ||
            token.Text.Length > 0 && (char.IsLetter(token.Text[0]) || token.Text[0] == '_') &&
            Lexer.GetKeyword(token.Text) == token.Kind;
        if (index > 0 && IsWord(ts[index - 1]) && ts[index - 1].End + 1 >= offset) { index--; start = ts[index].Start; }
        int end = index < ts.Length && IsWord(ts[index]) && ts[index].Start <= offset ? ts[index].End + 1 : offset;
        bool escapedPrefix = start > 0 && sources[path].Text[start - 1] == '@';
        if (escapedPrefix) start--;
        var range = replacementRange ?? Workspace.ToRange(sources[path].Span(start, end - start));
        if (ReflectionCompletion(path, offset, index, range, escapedPrefix: escapedPrefix) is { } reflectionCompletion) return reflectionCompletion;
        bool member = index > 0 && ts[index - 1].Kind is TokenKind.Dot or TokenKind.DoubleColon;
        var completionContext = CompletionContext(path, index);
        var items = Candidates(path, offset, index).Where(s => s.Syntax.Node is not ConstructorDeclaration && completionContext.Allows(s))
            .DistinctBy(s => (s.Name, member && s.BoundExtension ? "extension" : "symbol")).Select(s => (object)new {
                label = s.Name, kind = s.Kind, detail = Detail(s), filterText = escapedPrefix ? "@" + s.Name : s.Name,
                documentation = Documentation(s) is { } prose ? new Markup("plaintext", prose) : null,
                textEdit = new { range, newText = CompletionSpelling(s, escapedPrefix) }
            }).ToList();
        if (!escapedPrefix && !member && !completionContext.Restricted)
        {
            bool inBody = ContextParents(path, offset).Any(p => p.Node is BlockStatement);
            string[] keywords = inBody ? ["if", "else", "for", "foreach", "while", "return", "new", "move", "delete", "defer", "try", "catch", "throw", "default", "this", "null", "true", "false", "sizeof", "alignof", "typeof", "reflect", "nameof", "var", "alias", "constrained"] : ["namespace", "using", "class", "struct", "interface", "enum", "public", "private", "static", "inline", "extern", "readonly", "immutable", "ref", "copy", "const", "expect", "weak", "replace", "extension", "alias", "constrained"];
            foreach (var word in keywords.Concat(new[] { "void", "bool", "char", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "extralong", "nint", "nuint", "float", "double", "reflection", "readonly", "immutable", "managed" }).Distinct())
                items.Add(new { label = word, kind = 14, textEdit = new { range, newText = word } });
            if (index >= 2 && ts[index - 1].Kind == TokenKind.Colon && ts[index - 2].Kind == TokenKind.CloseParen)
                items.Add(new { label = "base", kind = 14, textEdit = new { range, newText = "base" } });
        }
        return new { isIncomplete = false, items };
    }

    private bool InCommentOrLiteral(string path, int offset)
    {
        var ts = tokens[path];
        int indexBefore = TokenStartIndex(ts, offset) - 1;
        var token = indexBefore >= 0 ? ts[indexBefore] : null;
        if (token != null && token.End >= offset) return token.Kind is TokenKind.StringLiteral or TokenKind.CharLiteral;
        // Comments are trivia, so inspect only the gap after the preceding token.
        int start = token?.End + 1 ?? 0;
        var trivia = sources[path].Text.AsSpan(start, offset - start);
        for (int index = 0; index < trivia.Length; index++)
        {
            // Recovery omits malformed literals from the token stream. Their
            // opening quote still suppresses code completion on that line.
            if (trivia[index] is '"' or '\'' && (index == 0 || trivia[index - 1] != '$'))
            {
                char quote = trivia[index++];
                for (; index < trivia.Length && trivia[index] is not ('\r' or '\n'); index++)
                {
                    if (trivia[index] == '\\') { index++; continue; }
                    if (trivia[index] == quote) break;
                }
                if (index >= trivia.Length) return true;
                continue;
            }
            if (index + 1 == trivia.Length) break;
            if (trivia[index] != '/') continue;
            if (trivia[index + 1] == '/')
            {
                int end = trivia[(index + 2)..].IndexOfAny('\r', '\n');
                if (end < 0) return true;
                index += end + 2;
            }
            else if (trivia[index + 1] == '*')
            {
                int end = trivia[(index + 2)..].IndexOf("*/", StringComparison.Ordinal);
                if (end < 0) return true;
                index += end + 3;
            }
        }
        return false;
    }
    public object? Hover(string path, int offset, VisualStudioHover? presentation = null, HoverOptions? options = null)
    {
        options ??= new();
        if (DocumentationReferenceAt(path, offset) is { } documented)
        {
            var signature = Signature(documented.Symbol, navigable: true, hoverPath: path, hoverOffset: offset);
            return new HoverResult(new("markdown", "```gflat\n" + Detail(documented.Symbol) + "\n```"), Workspace.ToRange(documented.Span),
                presentation?.Render(signature, [], Glyph(documented.Symbol)));
        }
        if (MacroAt(path, offset) is { Classification: "preprocessor" }) return options.ShowLanguageConstructs ? LanguageConstructHover(path, offset, presentation) : null;
        if (MacroHover(path, offset, presentation) is { } macro) return macro;
        var query = QueryHover(path, offset, presentation, options.ShowTypeLayout);
        if (query != null) return query;
        if (ReflectionMemberHover(path, offset, presentation) is { } reflectionMember) return reflectionMember;
        var inferred = InferredTypeHover(path, offset, presentation, options.ShowTypeLayout);
        if (inferred != null) return inferred;
        var compound = CompoundTypeHover(path, offset, presentation, options.ShowTypeLayout);
        if (compound != null) return compound;
        int index = TokenAt(path, offset);
        // On a qualified-name separator, show the entity to its right instead
        // of resolving the punctuation as a partial namespace expression.
        if (index >= 0 && tokens[path][index].Kind == TokenKind.DoubleColon && index + 1 < tokens[path].Length)
            index++;
        var symbol = index < 0 ? null : Resolve(path, index);
        if (symbol == null && index >= 0 && snapshot.Checker is { } literalChecker)
        {
            var literal = nodes[path].Where(n => Contains(n, offset) && n.Node is LiteralExpression or PrefixedStringLiteralExpression or InterpolatedStringExpression)
                .MinBy(n => n.Span.Length);
            if (literal?.Parent?.Node is PrefixedStringLiteralExpression) literal = literal.Parent;
            if (literal != null)
            {
                var literalType = literalChecker.GetType(literal.Node);
                if (TypeChecker.TypeName(literalType) == "<error>" && literal.Node is LiteralExpression { Token.Kind: TokenKind.True or TokenKind.False })
                    literalType = new NamedTypeExpression("bool", null, literal.Node.Line);
                if (TypeChecker.TypeName(literalType) != "<error>")
                {
                    var literalParts = TypeDisplayParts(literalType, path, offset);
                    string value = sources[path].Text.Substring(literal.Span.Start, Math.Min(literal.Span.Length, 100));
                    value = value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                    if (literal.Span.Length > 100) value += "...";
                    string kind = value is "true" or "false" or "null" ? "keyword" :
                        literal.Node is LiteralExpression { Token.Kind: TokenKind.CharLiteral } ? "string" :
                        literal.Node is LiteralExpression { Token.Kind: not TokenKind.StringLiteral } ? "number" : "string";
                    literalParts = [new("punctuation", "("), .. literalParts, new("punctuation", ")"), new(kind, value)];
                    string preview = string.Concat(literalParts.Select(p => p.Text));
                    return new HoverResult(new("markdown", "```gflat\n" + preview + "\n```"), Workspace.ToRange(literal.Span),
                        presentation?.Render(literalParts, [], "constant.public"));
                }
            }
        }
        if (symbol == null && index >= 0 && (TypeChecker.IsPrimitive(tokens[path][index].Text) || tokens[path][index].Kind == TokenKind.Reflection) && (tokens[path][index].Kind != TokenKind.Identifier || nodes[path].Any(n => n.Node is NamedTypeExpression && Contains(n, offset))))
        {
            string name = tokens[path][index].Text;
            var type = new NamedTypeExpression(name, null, 0);
            HoverDetail[] layout = [];
            if (name != "reflection" && options.ShowTypeLayout && snapshot.Checker is { } checker)
            {
                try { layout = [new("Size", $"{checker.GetTypeSize(type)} bytes"), new("Alignment", $"{checker.GetTypeAlignment(type)} bytes")]; }
                catch (CompileExceptions.TypeCheckException) { /* Some reserved type names have no concrete layout. */ }
            }
            return new HoverResult(new("markdown", "```gflat\n" + name + "\n```\n\n" + string.Join("  \n", layout.Select(d => d.Markdown))),
                Workspace.ToRange(tokens[path][index].Span), presentation?.Render([new("keyword", name)], layout, "struct.public"));
        }
        if (symbol == null) return options.ShowLanguageConstructs ? LanguageConstructHover(path, offset, presentation) : null;
        var parts = Signature(symbol, navigable: true, hoverPath: path, hoverOffset: offset);
        if (symbol.Parameters != null) parts = [.. parts, new("punctuation", ";")];
        HoverDetail[] details = options.ShowTypeLayout && !symbol.IsTypeParameter ? LayoutDetails(symbol) : [];
        if (Documentation(symbol) is { } prose)
            details = [new("", DocumentationMarkdown(prose), DocumentationParts(symbol, StructuredDocumentation(symbol)!.Summary)), .. details];
        if (StructuredDocumentation(symbol) is { Sections.Length: > 0 })
        {
            var document = DocumentationLocation(symbol);
            details = [.. details, new("", "[View documentation](" + document.Uri + ")", [new("text", "View documentation", Target: document, NavigationHint: "View documentation")])];
        }
        if (!symbol.IsTypeParameter && options.ShowKnownExceptions && (symbol.BoundMethod ?? symbol.Syntax.Node as MethodDeclaration) is { } hoveredMethod && snapshot.Checker is { } exceptionChecker &&
            !snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var exceptions = exceptionChecker.GetPossibleExceptions(hoveredMethod).Where(e => e != TypeChecker.UnknownExceptionType).ToArray();
            if (exceptions.Length > 0)
            {
                details = [.. details, new("", "Known exceptions:")];
                foreach (string exception in exceptions)
                {
                    string name = exceptionChecker.DisplayName(exception);
                    details = [.. details, new("", "  " + name, [new("text", "  "), new("class", name, Target: SymbolLocation(TypeSymbol(name, symbol)))])];
                }
            }
        }
        if (symbol.Parameters != null)
        {
            int count = (CallableFamily(tokens[path][index]) is { } family
                ? family.Length
                : Candidates(path, offset, index).Where(s => s.Parameters != null && s.Qualified == symbol.Qualified)
                    .Append(symbol).DistinctBy(Detail).Count()) - 1;
            if (count > 0) details = [.. details, new("", $"+{count} " + (count == 1 ? "overload" : "overloads"))];
        }
        string markdown = "```gflat\n" + string.Concat(parts.Select(p => p.Text)) + "\n```" + (details.Length == 0 ? "" : "\n\n" + string.Join("  \n", details.Select(d => d.Markdown)));
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(tokens[path][index].Span),
            presentation?.Render(parts, details, Glyph(symbol)));
    }
    public object? Definition(string path, int offset)
    {
        if (DocumentationReferenceAt(path, offset) is { } documentation) return NavigationLocation(documentation.Symbol);
        if (OverriddenMember(path, offset) is { } overridden) return NavigationLocation(overridden);
        if (MacroAt(path, offset) is { Declaration.Source: { } file } macro && !file.Path.StartsWith('<'))
            return new Location(Workspace.FileUri(file.Path), Workspace.ToRange(macro.Declaration));
        int index = TokenAt(path, offset);
        var symbol = index < 0 ? null : Resolve(path, index);
        return symbol?.NameSpan.Source is not SourceFile source || source.Path.StartsWith('<') ? null : new Location(Workspace.FileUri(source.Path), Workspace.ToRange(symbol.NameSpan));
    }
    public object? SignatureHelp(string path, int offset, bool visualStudio = false)
    {
        var ts = tokens[path];
        var stack = new Stack<(int Index, int Commas)>();
        for (int i = 0; i < ts.Length && ts[i].Start < offset; i++)
        {
            if (ts[i].Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace) stack.Push((i, 0));
            else if (ts[i].Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace) { if (stack.Count > 0) stack.Pop(); }
            else if (ts[i].Kind == TokenKind.Comma && !typeArgumentSeparators.Contains(ts[i]) && stack.Count > 0) { var frame = stack.Pop(); stack.Push((frame.Index, frame.Commas + 1)); }
        }
        var call = stack.FirstOrDefault(f => ts[f.Index].Kind == TokenKind.OpenParen &&
            (constructorArgumentLists.ContainsKey(ts[f.Index]) || CallableNameBeforeParenthesis(ts, f.Index) >= 0), (Index: -1, Commas: 0));
        if (call.Index < 0) return null;
        int nameIndex = constructorArgumentLists.TryGetValue(ts[call.Index], out var creationToken)
            ? Array.IndexOf(ts, creationToken) : CallableNameBeforeParenthesis(ts, call.Index);
        var candidates = CallableFamily(ts[nameIndex]) ?? (ts[nameIndex].Kind == TokenKind.Base
            ? BaseConstructors(path, offset).ToArray()
            : Candidates(path, offset, nameIndex).Where(s => s.Name == ts[nameIndex].Text && s.Parameters != null).ToArray());
        if (candidates.Length == 0) return null;
        var selected = Resolve(path, nameIndex);
        int active = Math.Max(0, Array.FindIndex(candidates, candidate => selected != null && candidate.Syntax == selected.Syntax));
        return new { signatures = candidates.Select(s => new {
            label = Detail(s),
            documentation = Documentation(s) is { } summary ? new Markup("plaintext", summary) : null,
            parameters = s.Parameters!.Select(p => new { label = ParameterText(p, s), documentation = ParameterDocumentation(s, p.Name) is { } description ? new Markup("plaintext", description) : null }).ToArray(),
            _vs_colorizedLabel = visualStudio ? VisualStudioHover.Text(Signature(s, navigable: true)) : null
        }).ToArray(), activeSignature = active, activeParameter = call.Commas };
    }
    public object SemanticTokens(string path, TextRange? range = null, bool visualStudio = false)
    {
        var data = new List<int>();
        var classified = new List<(SourceSpan Span, int Kind, int Modifiers)>();
        var macroTokens = MacroSourceClassifications(path);
        var macroStarts = macroTokens.Select(token => token.Span.Start).ToArray();
        bool MacroCovers(int offset)
        {
            int index = Array.BinarySearch(macroStarts, offset);
            if (index < 0) index = ~index - 1;
            return index >= 0 && offset < macroTokens[index].Span.Start + macroTokens[index].Span.Length;
        }
        int previousLine = 0, previousColumn = 0;
        var ts = tokens[path];
        var placementKeywords = nodes[path].Select(n => n.Node).OfType<NewExpression>()
            .Where(n => n.PlacementKeywordSpan.Length > 0).Select(n => n.PlacementKeywordSpan.Start).ToHashSet();
        var accessors = nodes[path].Select(n => n.Node).OfType<PropertyDeclaration>()
            .SelectMany(p => new[] { (p.Getter, "get"), (p.Setter, "set") })
            .Where(a => a.Item1 != null)
            .Select(a => ts.SkipWhile(t => t.Start < a.Item1!.Span.Start)
                .SkipWhile(t => t.Kind is TokenKind.Readonly or TokenKind.Immutable or TokenKind.Public or TokenKind.Private or TokenKind.Protected or TokenKind.Internal)
                .FirstOrDefault() is { } t && t.Text == a.Item2 ? t.Start : -1).ToHashSet();
        for (int i = 0; i < ts.Length; i++)
        {
            var span = ts[i].Span;
            if (ts[i].Kind == TokenKind.Identifier && span.Source is { } source && span.Start > 0 && source.Text[span.Start - 1] == '@')
                span = source.Span(span.Start - 1, span.Length + 1);
            int line = span.Line - 1, column = span.Column - 1;
            // Resolve only the requested visible range. A viewport request must
            // not bind every identifier in the rest of a large file first.
            if (range != null && (line < range.Start.Line || line == range.Start.Line && column < range.Start.Character || line > range.End.Line || line == range.End.Line && column >= range.End.Character)) continue;
            bool control = ts[i].Kind is TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.Foreach or TokenKind.While or TokenKind.Return or TokenKind.Break or TokenKind.Continue or TokenKind.Try or TokenKind.Catch or TokenKind.Throw or TokenKind.Defer;
            bool reinterpret = ts[i].Text == "reinterpret" && i > 0 && ts[i - 1].Kind == TokenKind.Dot &&
                Resolve(path, i)?.Syntax.Node is not (FieldDeclaration or PropertyDeclaration);
            bool modifier = reinterpret || accessors.Contains(ts[i].Start) || ts[i].Kind is TokenKind.Default or TokenKind.New or TokenKind.Delete or TokenKind.Sizeof or TokenKind.Alignof or TokenKind.Typeof or TokenKind.Nameof or TokenKind.NoThrow or TokenKind.Inline or TokenKind.Managed or TokenKind.Reflection or TokenKind.Reflect || placementKeywords.Contains(ts[i].Start) || IsValueModelKeyword(path, ts[i]);
            bool reflectionMember = reflectionMemberTokens.Contains(ts[i]);
            if (ts[i].Kind != TokenKind.Identifier && !control && !modifier) continue;
            if (MacroCovers(ts[i].Start)) continue;
            var symbol = control || modifier ? null : Resolve(path, i);
            if (symbol == null && !control && !modifier && !reflectionMember) continue;
            int kind = modifier ? Array.IndexOf(TokenTypes, "modifier") : control ? Array.IndexOf(TokenTypes, "keyword") : reflectionMember ? Array.IndexOf(TokenTypes, "property") : Array.IndexOf(TokenTypes, symbol!.Classification);
            if (kind < 0) continue;
            int modifiers = symbol?.NameSpan.Source?.Path == path && symbol.NameSpan.Start == ts[i].Start ? 1 : 0;
            if (symbol?.Static == true) modifiers |= 2;
            if (symbol?.Syntax.Node is FieldDeclaration { IsReadOnly: true } or FieldDeclaration { IsConst: true } or VariableDeclaration { IsConst: true }) modifiers |= 4;
            classified.Add((span, kind, modifiers));
        }
        classified.AddRange(macroTokens);
        foreach (var reference in documentationReferences)
            if (Workspace.Paths.Equals(reference.Key.Source?.Path, path))
                classified.Add((reference.Key, Array.IndexOf(TokenTypes, reference.Value.Classification), 0));
        foreach (var item in classified.OrderBy(t => t.Span.Start))
        {
            // VS maps the LSP number token to generic "literal", which masks
            // TextMate's native "number" classification. Keep its lexical color.
            if (visualStudio && item.Kind >= 0 && TokenTypes[item.Kind] == "number") continue;
            int line = item.Span.Line - 1, column = item.Span.Column - 1;
            if (item.Kind < 0 || range != null && (line < range.Start.Line || line == range.Start.Line && column < range.Start.Character || line > range.End.Line || line == range.End.Line && column >= range.End.Character)) continue;
            data.AddRange([line - previousLine, line == previousLine ? column - previousColumn : column, item.Span.Length, item.Kind, item.Modifiers]);
            previousLine = line; previousColumn = column;
        }
        return new { data };
    }
}
