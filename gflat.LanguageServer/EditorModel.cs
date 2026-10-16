using gflat.ast;
using gflat.diagnostics;
using gflat.CompileExceptions;
using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

/// <summary>Read-only editor queries over one analyzed set of source snapshots.</summary>
public sealed partial class EditorModel
{
    public static readonly string[] TokenTypes = ["namespace", "class", "struct", "interface", "enum", "type", "parameter", "variable", "property", "function", "method", "enumMember", "keyword", "modifier", "typeParameter"];
    public static readonly string[] VisualStudioTokenTypes = ["namespace name", "class name", "struct name", "interface name", "enum name", "type", "parameter name", "local name", "property name", "method name", "method name", "enum member name", "keyword - control", "keyword", "type parameter name"];
    public static readonly string[] TokenModifiers = ["declaration", "static", "readonly"];
    private sealed record Symbol(string Name, int Kind, string Classification, Syntax Syntax, SourceSpan NameSpan,
        string Namespace, string? Owner, Syntax? Scope, TypeExpression? Type, bool Static, TokenKind Access)
    {
        public string Qualified => Scope != null ? Name : Join(Owner ?? Namespace, Name);
        public IReadOnlyList<Parameter>? BoundParameters { get; init; }
        public IReadOnlyDictionary<string, TypeExpression>? GenericBindings { get; init; }
        public MethodDeclaration? BoundMethod { get; init; }
        public bool BoundExtension { get; init; }
        public bool IsTypeParameter { get; init; }
        public TypeExpression? TypeConstraint { get; init; }
        public IReadOnlyList<Parameter>? Parameters => IsTypeParameter ? null : BoundParameters ?? (Syntax.Node switch { MethodDeclaration m => m.Parameters, ExternDeclaration e => e.Parameters, ConstructorDeclaration c => c.Parameters, _ => null });
    }
    private readonly AnalysisSnapshot snapshot;
    private readonly Dictionary<string, SourceFile> sources;
    private readonly Dictionary<string, Token[]> tokens = new(Workspace.Paths);
    private readonly Dictionary<string, Syntax[]> nodes = new(Workspace.Paths);
    private readonly List<Symbol> symbols = [];
    private readonly Dictionary<(Type Kind, SourceSpan Span), Symbol> sourceTypeDeclarations = [];
    private readonly Dictionary<AstNode, Symbol> declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Symbol?> references = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Syntax, int> scopeEnds = new(ReferenceEqualityComparer.Instance);

    public EditorModel(AnalysisSnapshot snapshot, IEnumerable<SourceFile> files)
    {
        this.snapshot = snapshot;
        var allFiles = files.Concat(snapshot.Nodes.Select(n => n.Span.Source).OfType<SourceFile>())
            .DistinctBy(s => s.Path, Workspace.Paths).ToArray();
        sources = allFiles.ToDictionary(s => s.Path, Workspace.Paths);
        foreach (var file in allFiles)
        {
            try { tokens[file.Path] = Lexer.Tokenize(file, new DiagnosticBag()).Where(t => t.Kind != TokenKind.EndOfFile).ToArray(); }
            catch (TypeCheckException) { tokens[file.Path] = []; }
            nodes[file.Path] = snapshot.Nodes.Where(n => n.Span.Source?.Id == file.Id).ToArray();
            var braces = new Stack<int>();
            foreach (var token in tokens[file.Path])
            {
                if (token.Kind == TokenKind.OpenBrace) braces.Push(token.Start);
                else if (token.Kind == TokenKind.CloseBrace && braces.Count > 0) braces.Pop();
            }
            // Missing closing braces are routine while typing. The parser's
            // consumed-token range excludes trailing trivia, but the scope extends
            // through the caret at EOF until that brace is supplied.
            foreach (var node in nodes[file.Path].Where(n => n.Node is BlockStatement or MethodDeclaration or ConstructorDeclaration or ClassDeclaration or StructDeclaration or InterfaceDeclaration or NamespaceDeclaration or PropertyDeclaration or LambdaExpression or OperatorDeclaration))
                if (braces.Any(start => start >= node.Span.Start && start < node.Span.Start + node.Span.Length))
                    scopeEnds[node] = file.Text.Length + 1;
        }
        foreach (var entry in snapshot.Nodes)
        {
            var (name, kind, classification, type) = Describe(entry.Node);
            var parents = Parents(entry).ToArray();
            if (entry.Node is ConstructorDeclaration)
                name = parents.Where(parent => IsType(parent.Node)).Select(parent => SourceName(parent.Node)).FirstOrDefault() ?? name;
            if (name == null || name.Contains('$')) continue;
            string ns = snapshot.Checker?.DeclarationNamespace(entry.Node) ?? Namespace(parents);
            string? owner = Owner(parents);
            if (owner == null && entry.Node is MethodDeclaration qualified && snapshot.Checker != null &&
                snapshot.Checker.TryGetMethodOwner(qualified, out string methodOwner, out _))
                owner = snapshot.Checker.DisplayTypeName(new NamedTypeExpression(methodOwner, null, qualified.Line));
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
                ForeachStatement or CatchClause => entry,
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
            declarations.TryAdd(entry.Node, symbol);
        }
        RegisterTypeParameters();
        // Resolve once on the analysis worker. Hover and semantic-token requests
        // must not repeatedly walk the tree on the server's message loop.
        foreach (var (path, ts) in tokens)
            for (int i = 0; i < ts.Length; i++)
                if (ts[i].Kind == TokenKind.Identifier) references[ts[i]] = ResolveCore(path, i);
    }

    private string ParameterText(Parameter p) => $"{(p.IsExtensionReceiver ? "this " : "")}{(p.IsConst ? "const " : "")}{DisplayType(p.Type)} {p.Name}" +
        (p.DefaultValue == null ? "" : " = " + ParameterDefaultText(p));
    private static string Join(string left, string right) => left.Length == 0 ? right : left + "::" + right;
    private static IEnumerable<Syntax> Parents(Syntax node) { for (var p = node.Parent; p != null; p = p.Parent) yield return p; }
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
        FieldDeclaration f => (f.Name, 5, "property", f.Type),
        PropertyDeclaration p => (p.Name, 10, "property", p.Type),
        VariableDeclaration v => (v.Name, 6, "variable", v.Type),
        Parameter p => (p.Name, 6, "parameter", p.Type),
        EnumMemberDeclaration e => (e.Name, 20, "enumMember", null),
        AliasDeclaration a => (a.Name, 7, "type", a.TargetType),
        ForeachStatement f => (f.VariableName, 6, "variable", f.ElementType),
        CatchClause c => (c.VariableName, 6, "variable", c.ExceptionType),
        _ => (null, 0, "", null)
    };

    private SourceSpan NameSpan(Syntax node, string name, TypeExpression? type)
    {
        var span = node.Span;
        if (span.Source == null || !tokens.TryGetValue(span.Source.Path, out var fileTokens)) return span with { Length = 0 };
        var candidates = fileTokens.Where(t => t.Start >= span.Start && t.End < span.Start + span.Length && t.Text == name).ToArray();
        // In `String String`, the first occurrence belongs to the type.
        if (type != null && type.Span.Source == span.Source)
        {
            var afterType = candidates.FirstOrDefault(t => t.Start >= type.Span.Start + type.Span.Length);
            if (afterType != null) return afterType.Span;
        }
        return candidates.FirstOrDefault()?.Span ?? span with { Length = 0 };
    }

    private bool Contains(Syntax node, int offset) => offset >= node.Span.Start && offset < scopeEnds.GetValueOrDefault(node, node.Span.Start + node.Span.Length);
    private Syntax? Context(string path, int offset) => nodes.GetValueOrDefault(path)?.Where(n => Contains(n, offset)).MinBy(n => scopeEnds.GetValueOrDefault(n, n.Span.Start + n.Span.Length) - n.Span.Start);
    private IEnumerable<Symbol> Visible(string path, int offset)
    {
        var context = Context(path, offset);
        var parents = context == null ? [] : new[] { context }.Concat(Parents(context)).ToArray();
        string ns = Namespace(parents);
        string? owner = Owner(parents);
        bool isStatic = parents.Select(p => p.Node).OfType<MethodDeclaration>().FirstOrDefault()?.IsStatic == true;
        var imports = snapshot.Imports.GetValueOrDefault(sources[path].Id) ?? [];
        var inheritedOrder = owner == null ? Array.Empty<Symbol>() : TypeMembers(owner)
            .Where(s => Accessible(s, path, offset)).ToArray();
        var inherited = inheritedOrder.ToHashSet();
        return symbols.Where(s => s.Scope != null
            ? s.NameSpan.Source?.Path == path && Contains(s.Scope, offset) && (s.IsTypeParameter || s.Syntax.Node is Parameter || s.NameSpan.Start <= offset)
            : s.Owner != null ? inherited.Contains(s) && (!isStatic || s.Static || IsType(s.Syntax.Node))
            : s.Namespace.Length == 0 || ns == s.Namespace || ns.StartsWith(s.Namespace + "::", StringComparison.Ordinal) || imports.Contains(s.Namespace))
            .Where(s => s.Syntax.Node is not (ExternDeclaration or PropertyDeclaration or FieldDeclaration) || Accessible(s, path, offset))
            .OrderBy(s => s.Scope == null ? int.MaxValue : s.Scope.Span.Length)
            .ThenBy(s => inherited.Contains(s) ? Array.IndexOf(inheritedOrder, s) : int.MaxValue)
            .ThenByDescending(s => s.NameSpan.Start);
    }

    private IEnumerable<Symbol> Members(string path, int offset, int separatorIndex)
    {
        var ts = tokens[path];
        if (separatorIndex < 1) return [];
        var separator = ts[separatorIndex];
        var left = ts[separatorIndex - 1];
        if (separator.Kind == TokenKind.DoubleColon)
        {
            int start = separatorIndex - 1;
            while (start >= 2 && ts[start - 1].Kind == TokenKind.DoubleColon) start -= 2;
            string qualifier = string.Concat(ts[start..separatorIndex].Select(t => t.Text));
            if (qualifier.StartsWith("global::")) qualifier = qualifier[8..];
            if (qualifier == "global") qualifier = "";
            var resolved = Visible(path, offset).FirstOrDefault(s => s.Name == qualifier && (s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration || s.Classification == "namespace"))
                ?? symbols.FirstOrDefault(s => s.Qualified == qualifier && s.Syntax.Node is AliasDeclaration);
            if (resolved != null && ContractOwners(resolved, path, offset) is { } owners)
            {
                return symbols.Where(s => owners.Contains(s.Owner) && s.Static && Accessible(s, path, offset));
            }
            qualifier = resolved is { IsTypeParameter: true }
                ? TypeOwner(resolved.TypeConstraint, path, offset) ?? qualifier
                : resolved?.Syntax.Node is AliasDeclaration
                    ? TypeOwner(resolved.Type, path, offset) ?? qualifier
                    : resolved?.Qualified ?? qualifier;
            return symbols.Where(s => s.Scope == null &&
                (s.Namespace == qualifier && s.Owner == null || s.Owner == qualifier && (s.Static || IsType(s.Syntax.Node))) && Accessible(s, path, offset));
        }
        var expression = nodes[path].Where(n => n.Node is IdentifierExpression or CallExpression or MemberAccessExpression or NewExpression or IndexExpression or UnaryExpression or CastExpression or TypeofExpression or LiteralExpression or PrefixedStringLiteralExpression or InterpolatedStringExpression && n.Span.Start + n.Span.Length == left.End + 1)
            .OrderByDescending(n => n.Span.Length).FirstOrDefault();
        TypeExpression? type = expression == null ? null : ExpressionType(expression.Node, path, offset);
        if (type == null)
            type = Visible(path, offset).FirstOrDefault(s => s.Name == left.Text)?.Type;
        string? owner = TypeOwner(type, path, offset);
        if (left.Text == "this") owner = Owner(ContextParents(path, offset));
        var members = owner == null ? [] : TypeMembers(owner).Where(s => !s.Static && !IsType(s.Syntax.Node) && Accessible(s, path, offset)).ToArray();
        return members.Concat(ExtensionMembers(path, offset, expression?.Node, type)
            .Where(extension => !members.Any(member => member.Name == extension.Name && IsCallableMember(member))));
    }

    private HashSet<string?>? ContractOwners(Symbol alias, string path, int offset)
    {
        var seen = new HashSet<Symbol>();
        while (alias.Syntax.Node is AliasDeclaration declaration && seen.Add(alias))
        {
            if (declaration.ImplementationKind == FunctionImplementationKind.Replace &&
                symbols.FirstOrDefault(s => s.Qualified == alias.Qualified &&
                    s.Syntax.Node is AliasDeclaration { ImplementationKind: not FunctionImplementationKind.Replace }) is { } original)
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
            var next = Visible(declaringPath, declaringOffset).FirstOrDefault(s => s.Name == targetName && s.Syntax.Node is AliasDeclaration)
                ?? symbols.FirstOrDefault(s => s.Qualified == targetName && s.Syntax.Node is AliasDeclaration);
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
        if (node is IdentifierExpression identifier) return Visible(path, offset).FirstOrDefault(s => s.Name == identifier.Name)?.Type;
        if (node is NewExpression creation) return creation.Type;
        if (node is CastExpression cast) return cast.TargetType;
        if (node is UnaryExpression { Operator: TokenKind.Bang, IsPrefix: false } suppression)
            return ExpressionType(suppression.Operand, path, offset);
        if (node is UnaryExpression { Operator: TokenKind.Star } dereference)
        {
            var operand = ExpressionType(dereference.Operand, path, offset);
            return operand is PointerTypeExpression pointer ? pointer.Inner : null;
        }
        if (node is MemberAccessExpression member)
        {
            string? owner = TypeOwner(ExpressionType(member.Object, path, offset), path, offset);
            return owner == null ? null : TypeMembers(owner).FirstOrDefault(s => s.Name == member.Member)?.Type;
        }
        if (node is CallExpression call)
        {
            var ts = tokens[path];
            int index = Array.FindLastIndex(ts, t => t.Kind == TokenKind.Identifier && t.End + 1 == call.Callee.Span.Start + call.Callee.Span.Length);
            if (index >= 0)
            {
                var returns = Candidates(path, offset, index).Where(s => s.Name == ts[index].Text && s.Parameters != null)
                    .Select(s => s.Type).OfType<TypeExpression>().DistinctBy(TypeChecker.TypeName).ToArray();
                if (returns.Length == 1) return returns[0];
            }
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
        if ((s.Syntax.Node is ExternDeclaration || s.Syntax.Node is PropertyDeclaration or FieldDeclaration && s.Owner == null) && s.Access == TokenKind.Private)
            return Workspace.Paths.Equals(s.Syntax.Span.Source?.Path, path);
        string? owner = Owner(ContextParents(path, offset));
        return s.Access is TokenKind.Public or TokenKind.Internal || s.Owner == null || s.Owner == owner ||
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
                string qualified = TemplateName(DisplayType(nested.WithReadOnlyValue(false)));
                var target = symbols.FirstOrDefault(s => s.Qualified == qualified);
                if (target?.Syntax.Node is AliasDeclaration)
                {
                    if (!aliases.Add(target)) return null;
                    type = target.Type;
                    continue;
                }
                return target != null && IsType(target.Syntax.Node) ? target.Qualified : null;
            }
            if (type is not NamedTypeExpression named) return null;
            string name = named.Namespace == null ? named.Name : Join(named.Namespace, named.Name);
            var alias = symbols.FirstOrDefault(s => s.Syntax.Node is AliasDeclaration && s.Qualified == name)
                ?? Visible(path, offset).FirstOrDefault(s => s.Syntax.Node is AliasDeclaration && s.Name == name);
            if (alias != null)
            {
                if (!aliases.Add(alias)) return null;
                type = alias.Type;
                continue;
            }
            // Specialized declarations retain their original source spans. Use
            // their registered origin before falling back to incomplete-source
            // name recovery, which cannot distinguish a nested specialization
            // from its generic containing type.
            if (snapshot.Checker?.GetTypeDeclaration(named.Name) is { } declaration &&
                sourceTypeDeclarations.TryGetValue((declaration.GetType(), declaration.Span), out var sourceType))
                return sourceType.Qualified;
            // Source declarations retain their internal names after checking. Match
            // those before decoding a specialization, even when checking is incomplete.
            var internalMatch = symbols.Where(s => IsType(s.Syntax.Node)).Select(s => (Symbol: s, Name: s.Syntax.Node switch {
                ClassDeclaration c => c.Name, StructDeclaration st => st.Name, InterfaceDeclaration i => i.Name, EnumDeclaration e => e.Name, _ => "" }))
                .Where(s => named.Name == s.Name || named.Name.StartsWith(s.Name + "$", StringComparison.Ordinal))
                .OrderByDescending(s => s.Name.Length).FirstOrDefault();
            if (internalMatch.Symbol != null && (named.Namespace == null || named.Namespace == internalMatch.Symbol.Namespace)) return internalMatch.Symbol.Qualified;
            string display = TemplateName(DisplayType(named.WithReadOnlyValue(false)));
            return symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && (s.Qualified == name || s.Qualified == display))?.Qualified
                ?? Visible(path, offset).FirstOrDefault(s => IsType(s.Syntax.Node) && s.Name == name)?.Qualified;
        }
    }
    private IEnumerable<Symbol> TypeMembers(string owner)
    {
        var visited = new HashSet<string>();
        while (visited.Add(owner))
        {
            foreach (var symbol in symbols.Where(s => s.Owner == owner && s.Scope == null && s.Syntax.Node is not ConstructorDeclaration and not MethodDeclaration { StaticConstructorName: not null })) yield return symbol;
            var type = symbols.FirstOrDefault(s => s.Qualified == owner && s.Syntax.Node is ClassDeclaration);
            if (type?.Syntax.Node is not ClassDeclaration c) yield break;
            // The parser records the colon list in Interfaces; checking separates
            // its base class into ClassInfo without rewriting the declaration.
            string? baseClass = snapshot.Checker?.GetClass(owner)?.BaseClass ?? c.BaseClass ??
                c.Interfaces.FirstOrDefault(name => symbols.Any(s => s.Syntax.Node is ClassDeclaration && (s.Qualified == name || s.Qualified == Join(type.Namespace, name))));
            if (baseClass == null) yield break;
            owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) &&
                (s.Syntax.Node is ClassDeclaration baseDeclaration && baseDeclaration.Name == baseClass ||
                 s.Qualified == baseClass || s.Qualified == Join(type.Namespace, baseClass)))?.Qualified ?? baseClass;
        }
    }
    private IEnumerable<Symbol> Candidates(string path, int offset, int tokenIndex)
    {
        var ts = tokens[path];
        int previous = tokenIndex - 1;
        if (previous >= 0 && ts[previous].Kind is TokenKind.Dot or TokenKind.DoubleColon)
            return Members(path, offset, previous);
        return Visible(path, offset);
    }
    private Symbol? Resolve(string path, int index)
        => references.GetValueOrDefault(tokens[path][index]);

    private bool IsValueModelKeyword(string path, Token token)
    {
        if (token.Text is not ("move" or "copy" or "at" or "ref" or "this" or "with")) return false;
        foreach (var syntax in nodes[path])
        {
            if (token.Text == "this" && syntax.Node is Parameter { IsExtensionReceiver: true } && token.Start == syntax.Span.Start) return true;
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
        }
        return false;
    }

    private Symbol? ResolveCore(string path, int index)
    {
        var token = tokens[path][index];
        if (IsValueModelKeyword(path, token)) return null;
        if (token.Text == "at" && nodes[path].Any(n => n.Node is NewExpression placement &&
            placement.PlacementKeywordSpan.Length > 0 && placement.PlacementKeywordSpan.Start == token.Start)) return null;
        var declaration = symbols.FirstOrDefault(s => s.NameSpan.Source?.Path == path && s.NameSpan.Start == token.Start && s.NameSpan.Length == token.Span.Length);
        if (declaration != null) return declaration;
        var call = nodes[path].Where(n => n.Node is CallExpression c && c.Callee.Span.Start <= token.Start && c.Callee.Span.Start + c.Callee.Span.Length == token.End + 1).MinBy(n => n.Span.Length);
        if (call?.Node is CallExpression expression && ExtensionCallSymbol(expression) is { } extension) return extension;
        if (call?.Node is CallExpression resolvedCall && snapshot.Checker?.GetResolvedCall(resolvedCall) is AstNode target && declarations.TryGetValue(target, out var method)) return method;
        // A qualifier in a named type is a namespace/type, not the final type
        // token. Resolve it before filtering type annotations to type symbols.
        if (index + 1 < tokens[path].Length && tokens[path][index + 1].Kind == TokenKind.DoubleColon)
        {
            int first = index;
            while (first >= 2 && tokens[path][first - 1].Kind == TokenKind.DoubleColon) first -= 2;
            string qualified = string.Concat(tokens[path][first..(index + 1)].Select(t => t.Text));
            if (qualified.StartsWith("global::", StringComparison.Ordinal)) qualified = qualified[8..];
            var qualifier = symbols.FirstOrDefault(s => s.Syntax.Node is NamespaceDeclaration &&
                (s.Qualified == qualified || s.Name == qualified && s.Namespace.Length == 0));
            if (qualifier != null) return qualifier;
        }
        var candidates = Candidates(path, token.Start, index);
        // A local named `String` must not recolor a `String` type annotation.
        bool typePosition = nodes[path].Any(n => n.Node is NamedTypeExpression && n.Span.Length > 0 && token.Start >= n.Span.Start && token.End < n.Span.Start + n.Span.Length);
        if (typePosition)
            candidates = candidates.Where(s => s.IsTypeParameter || IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
        else if (candidates.FirstOrDefault(s => !s.IsTypeParameter && s.Name == token.Text) is { } value)
            return value;
        return candidates.FirstOrDefault(s => s.Name == token.Text);
    }
    private int TokenAt(string path, int offset) => Array.FindIndex(tokens[path], t => t.Start <= offset && t.End >= offset);

    public object Completion(string path, int offset)
    {
        var ts = tokens[path];
        if (InCommentOrLiteral(path, offset)) return new { isIncomplete = false, items = Array.Empty<object>() };
        int index = Array.FindIndex(ts, t => t.Start >= offset);
        if (index < 0) index = ts.Length;
        int start = offset;
        if (index > 0 && ts[index - 1].Kind == TokenKind.Identifier && ts[index - 1].End + 1 >= offset) { index--; start = ts[index].Start; }
        int end = index < ts.Length && ts[index].Kind == TokenKind.Identifier && ts[index].Start <= offset ? ts[index].End + 1 : offset;
        var range = Workspace.ToRange(sources[path].Span(start, end - start));
        bool member = index > 0 && ts[index - 1].Kind is TokenKind.Dot or TokenKind.DoubleColon;
        var completionContext = CompletionContext(path, index);
        var items = Candidates(path, offset, index).Where(s => s.Syntax.Node is not ConstructorDeclaration && completionContext.Allows(s))
            .DistinctBy(s => (s.Name, member && s.BoundExtension ? "extension" : "symbol")).Select(s => (object)new { label = s.Name, kind = s.Kind, detail = Detail(s), textEdit = new { range, newText = s.Name } }).ToList();
        if (!member && !completionContext.Restricted)
        {
            bool inBody = ContextParents(path, offset).Any(p => p.Node is BlockStatement);
            string[] keywords = inBody ? ["if", "else", "for", "foreach", "while", "return", "new", "move", "delete", "defer", "try", "catch", "throw", "this", "null", "true", "false", "sizeof", "alignof", "typeof", "nameof"] : ["namespace", "using", "class", "struct", "interface", "enum", "public", "private", "static", "inline", "extern", "readonly", "ref", "copy", "const", "expect", "weak", "replace"];
            foreach (var word in keywords.Concat(new[] { "void", "bool", "char", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double", "readonly", "managed" }).Distinct())
                items.Add(new { label = word, kind = 14, textEdit = new { range, newText = word } });
            if (index >= 2 && ts[index - 1].Kind == TokenKind.Colon && ts[index - 2].Kind == TokenKind.CloseParen)
                items.Add(new { label = "base", kind = 14, textEdit = new { range, newText = "base" } });
        }
        return new { isIncomplete = false, items };
    }

    private bool InCommentOrLiteral(string path, int offset)
    {
        var ts = tokens[path];
        var token = ts.FirstOrDefault(t => t.Start < offset && t.End >= offset);
        if (token != null) return token.Kind is TokenKind.StringLiteral or TokenKind.CharLiteral;
        // Comments are trivia, so inspect only the gap after the preceding token.
        int start = ts.LastOrDefault(t => t.End < offset)?.End + 1 ?? 0;
        string trivia = sources[path].Text[start..offset];
        int lineComment = trivia.LastIndexOf("//", StringComparison.Ordinal);
        if (lineComment >= 0 && !trivia[lineComment..].Contains('\n')) return true;
        return trivia.LastIndexOf("/*", StringComparison.Ordinal) > trivia.LastIndexOf("*/", StringComparison.Ordinal);
    }
    public object? Hover(string path, int offset, VisualStudioHover? presentation = null, HoverOptions? options = null)
    {
        options ??= new();
        var query = QueryHover(path, offset, presentation, options.ShowTypeLayout);
        if (query != null) return query;
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
        if (symbol == null && index >= 0 && TypeChecker.IsPrimitive(tokens[path][index].Text) && (tokens[path][index].Kind != TokenKind.Identifier || nodes[path].Any(n => n.Node is NamedTypeExpression && Contains(n, offset))))
        {
            string name = tokens[path][index].Text;
            var type = new NamedTypeExpression(name, null, 0);
            HoverDetail[] layout = [];
            if (options.ShowTypeLayout && snapshot.Checker is { } checker)
            {
                try { layout = [new("Size", $"{checker.GetTypeSize(type)} bytes"), new("Alignment", $"{checker.GetTypeAlignment(type)} bytes")]; }
                catch (CompileExceptions.TypeCheckException) { /* Some reserved type names have no concrete layout. */ }
            }
            return new HoverResult(new("markdown", "```gflat\n" + name + "\n```\n\n" + string.Join("  \n", layout.Select(d => d.Markdown))),
                Workspace.ToRange(tokens[path][index].Span), presentation?.Render([new("keyword", name)], layout, "struct.public"));
        }
        if (symbol == null) return null;
        var parts = Signature(symbol, navigable: true, hoverPath: path, hoverOffset: offset);
        if (symbol.Parameters != null) parts = [.. parts, new("punctuation", ";")];
        HoverDetail[] details = options.ShowTypeLayout && !symbol.IsTypeParameter ? LayoutDetails(symbol) : [];
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
            int count = Candidates(path, offset, index).Where(s => s.Parameters != null && s.Qualified == symbol.Qualified)
                .Append(symbol).DistinctBy(Detail).Count() - 1;
            if (count > 0) details = [.. details, new("", $"+{count} " + (count == 1 ? "overload" : "overloads"))];
        }
        string markdown = "```gflat\n" + string.Concat(parts.Select(p => p.Text)) + "\n```" + (details.Length == 0 ? "" : "\n\n" + string.Join("  \n", details.Select(d => d.Markdown)));
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(tokens[path][index].Span),
            presentation?.Render(parts, details, Glyph(symbol)));
    }
    public object? Definition(string path, int offset)
    {
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
            else if (ts[i].Kind == TokenKind.Comma && stack.Count > 0) { var frame = stack.Pop(); stack.Push((frame.Index, frame.Commas + 1)); }
        }
        var call = stack.FirstOrDefault(f => ts[f.Index].Kind == TokenKind.OpenParen && f.Index > 0 && ts[f.Index - 1].Kind is TokenKind.Identifier or TokenKind.Base, (Index: -1, Commas: 0));
        if (call.Index < 0) return null;
        int nameIndex = call.Index - 1;
        var candidates = ts[nameIndex].Kind == TokenKind.Base
            ? BaseConstructors(path, offset).ToArray()
            : Candidates(path, offset, nameIndex).Where(s => s.Name == ts[nameIndex].Text && s.Parameters != null).ToArray();
        if (candidates.Length == 0) return null;
        var selected = Resolve(path, nameIndex);
        int active = Math.Max(0, Array.IndexOf(candidates, selected));
        return new { signatures = candidates.Select(s => new {
            label = Detail(s),
            parameters = s.Parameters!.Select(p => new { label = ParameterText(p) }).ToArray(),
            _vs_colorizedLabel = visualStudio ? VisualStudioHover.Text(Signature(s, navigable: true)) : null
        }).ToArray(), activeSignature = active, activeParameter = call.Commas };
    }
    public object SemanticTokens(string path, TextRange? range = null)
    {
        var data = new List<int>();
        int previousLine = 0, previousColumn = 0;
        var ts = tokens[path];
        var placementKeywords = nodes[path].Select(n => n.Node).OfType<NewExpression>()
            .Where(n => n.PlacementKeywordSpan.Length > 0).Select(n => n.PlacementKeywordSpan.Start).ToHashSet();
        var accessors = nodes[path].Select(n => n.Node).OfType<PropertyDeclaration>()
            .SelectMany(p => new[] { (p.Getter, "get"), (p.Setter, "set") })
            .Where(a => a.Item1 != null)
            .Select(a => ts.SkipWhile(t => t.Start < a.Item1!.Span.Start)
                .SkipWhile(t => t.Kind is TokenKind.Readonly or TokenKind.Public or TokenKind.Private or TokenKind.Protected or TokenKind.Internal)
                .FirstOrDefault() is { } t && t.Text == a.Item2 ? t.Start : -1).ToHashSet();
        for (int i = 0; i < ts.Length; i++)
        {
            bool control = ts[i].Kind is TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.Foreach or TokenKind.While or TokenKind.Return or TokenKind.Break or TokenKind.Continue or TokenKind.Try or TokenKind.Catch or TokenKind.Throw or TokenKind.Defer;
            bool modifier = accessors.Contains(ts[i].Start) || ts[i].Kind is TokenKind.NoThrow or TokenKind.Inline or TokenKind.Managed || placementKeywords.Contains(ts[i].Start) || IsValueModelKeyword(path, ts[i]);
            if (ts[i].Kind != TokenKind.Identifier && !control && !modifier) continue;
            var symbol = control || modifier ? null : Resolve(path, i);
            if (symbol == null && !control && !modifier) continue;
            var span = ts[i].Span;
            int line = span.Line - 1, column = span.Column - 1;
            if (range != null && (line < range.Start.Line || line == range.Start.Line && column < range.Start.Character || line > range.End.Line || line == range.End.Line && column >= range.End.Character)) continue;
            int kind = modifier ? Array.IndexOf(TokenTypes, "modifier") : control ? Array.IndexOf(TokenTypes, "keyword") : Array.IndexOf(TokenTypes, symbol!.Classification);
            if (kind < 0) continue;
            int modifiers = symbol?.NameSpan.Source?.Path == path && symbol.NameSpan.Start == span.Start ? 1 : 0;
            if (symbol?.Static == true) modifiers |= 2;
            if (symbol?.Syntax.Node is FieldDeclaration { IsReadOnly: true } or FieldDeclaration { IsConst: true } or VariableDeclaration { IsConst: true }) modifiers |= 4;
            data.AddRange([line - previousLine, line == previousLine ? column - previousColumn : column, span.Length, kind, modifiers]);
            previousLine = line; previousColumn = column;
        }
        return new { data };
    }
}
