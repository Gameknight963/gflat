using gflat.ast;
using gflat.diagnostics;
using gflat.CompileExceptions;
using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

/// <summary>Read-only editor queries over one analyzed set of source snapshots.</summary>
public sealed partial class EditorModel
{
    public static readonly string[] TokenTypes = ["namespace", "class", "struct", "interface", "enum", "type", "parameter", "variable", "property", "function", "method", "enumMember", "keyword", "modifier"];
    public static readonly string[] VisualStudioTokenTypes = ["namespace name", "class name", "struct name", "interface name", "enum name", "type", "parameter name", "local name", "property name", "method name", "method name", "enum member name", "keyword - control", "keyword"];
    public static readonly string[] TokenModifiers = ["declaration", "static", "readonly"];
    private sealed record Symbol(string Name, int Kind, string Classification, Syntax Syntax, SourceSpan NameSpan,
        string Namespace, string? Owner, Syntax? Scope, TypeExpression? Type, bool Static, TokenKind Access)
    {
        public string Qualified => Scope != null ? Name : Join(Owner ?? Namespace, Name);
        public IReadOnlyList<Parameter>? Parameters => Syntax.Node switch { MethodDeclaration m => m.Parameters, ExternDeclaration e => e.Parameters, ConstructorDeclaration c => c.Parameters, _ => null };
    }
    private readonly AnalysisSnapshot snapshot;
    private readonly Dictionary<string, SourceFile> sources;
    private readonly Dictionary<string, Token[]> tokens = new(Workspace.Paths);
    private readonly Dictionary<string, Syntax[]> nodes = new(Workspace.Paths);
    private readonly List<Symbol> symbols = [];
    private readonly Dictionary<AstNode, Symbol> declarations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Token, Symbol?> references = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Syntax, int> scopeEnds = new(ReferenceEqualityComparer.Instance);

    public EditorModel(AnalysisSnapshot snapshot, IEnumerable<SourceFile> files)
    {
        this.snapshot = snapshot;
        sources = files.ToDictionary(s => s.Path, Workspace.Paths);
        foreach (var file in files)
        {
            try { tokens[file.Path] = Lexer.Tokenize(file).Where(t => t.Kind != TokenKind.EndOfFile).ToArray(); }
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
            foreach (var node in nodes[file.Path].Where(n => n.Node is BlockStatement or MethodDeclaration or ConstructorDeclaration or ClassDeclaration or StructDeclaration or InterfaceDeclaration or NamespaceDeclaration))
                if (braces.Any(start => start >= node.Span.Start && start < node.Span.Start + node.Span.Length))
                    scopeEnds[node] = file.Text.Length + 1;
        }
        foreach (var entry in snapshot.Nodes)
        {
            var (name, kind, classification, type) = Describe(entry.Node);
            if (name == null || name.Contains('$')) continue;
            var parents = Parents(entry).ToArray();
            string ns = Namespace(parents);
            string? owner = Owner(parents);
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
                Parameter => parents.FirstOrDefault(p => p.Node is MethodDeclaration or ExternDeclaration or ConstructorDeclaration or LambdaExpression or OperatorDeclaration),
                ForeachStatement or CatchClause => entry,
                _ => null
            };
            TypeExpression? declaredType = type;
            var nameSpan = NameSpan(entry, name, declaredType);
            if (entry.Node is NamespaceDeclaration) owner = null;
            var symbol = new Symbol(name, kind, classification, entry, nameSpan, ns, owner, scope, type,
                IsStatic(entry.Node), Access(entry.Node));
            symbols.Add(symbol);
            declarations.TryAdd(entry.Node, symbol);
        }
        // Resolve once on the analysis worker. Hover and semantic-token requests
        // must not repeatedly walk the tree on the server's message loop.
        foreach (var (path, ts) in tokens)
            for (int i = 0; i < ts.Length; i++)
                if (ts[i].Kind == TokenKind.Identifier) references[ts[i]] = ResolveCore(path, i);
    }

    private string ParameterText(Parameter p) => $"{(p.IsConst ? "const " : "")}{DisplayType(p.Type)} {p.Name}";
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
    private static TokenKind Access(AstNode n) => n switch { MethodDeclaration m => m.Accessibility, FieldDeclaration f => f.Accessibility, PropertyDeclaration p => p.Getter?.Accessibility ?? p.Setter?.Accessibility ?? TokenKind.Private, ClassDeclaration c => c.Accessibility, StructDeclaration s => s.Accessibility, InterfaceDeclaration i => i.Accessibility, EnumDeclaration e => e.Accessibility, ConstructorDeclaration c => c.Accessibility, _ => TokenKind.Public };
    private static (string? Name, int Kind, string Classification, TypeExpression? Type) Describe(AstNode node) => node switch
    {
        ClassDeclaration c => (c.SourceName, 7, "class", null),
        StructDeclaration s => (s.SourceName, 22, "struct", null),
        InterfaceDeclaration i => (i.SourceName, 8, "interface", null),
        EnumDeclaration e => (e.SourceName, 13, "enum", null),
        NamespaceDeclaration n => (n.Name, 9, "namespace", null),
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
        return symbols.Where(s => s.Scope != null
            ? s.NameSpan.Source?.Path == path && Contains(s.Scope, offset) && (s.Syntax.Node is Parameter || s.NameSpan.Start <= offset)
            : s.Owner != null ? s.Owner == owner && (!isStatic || s.Static || IsType(s.Syntax.Node))
            : s.Namespace.Length == 0 || ns == s.Namespace || ns.StartsWith(s.Namespace + "::", StringComparison.Ordinal) || imports.Contains(s.Namespace))
            .OrderBy(s => s.Scope == null ? int.MaxValue : s.Scope.Span.Length)
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
            var resolved = Visible(path, offset).FirstOrDefault(s => s.Name == qualifier && (IsType(s.Syntax.Node) || s.Classification == "namespace"));
            qualifier = resolved?.Qualified ?? qualifier;
            return symbols.Where(s => s.Scope == null &&
                (s.Namespace == qualifier && s.Owner == null || s.Owner == qualifier && (s.Static || IsType(s.Syntax.Node))) && Accessible(s, path, offset));
        }
        var expression = nodes[path].Where(n => n.Node is IdentifierExpression or CallExpression or MemberAccessExpression or NewExpression or IndexExpression or UnaryExpression or CastExpression && n.Span.Start + n.Span.Length == left.End + 1)
            .OrderByDescending(n => n.Span.Length).FirstOrDefault();
        TypeExpression? type = expression == null ? null : ExpressionType(expression.Node, path, offset);
        if (type == null)
            type = Visible(path, offset).FirstOrDefault(s => s.Name == left.Text)?.Type;
        string? owner = TypeOwner(type, path, offset);
        if (left.Text == "this") owner = Owner(ContextParents(path, offset));
        return owner == null ? [] : TypeMembers(owner).Where(s => !s.Static && !IsType(s.Syntax.Node) && Accessible(s, path, offset));
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
    private bool Accessible(Symbol s, string path, int offset) => s.Access == TokenKind.Public || s.Owner == null || s.Owner == Owner(ContextParents(path, offset));
    private string? TypeOwner(TypeExpression? type, string path, int offset)
    {
        while (type is PointerTypeExpression or ManagedTypeExpression)
            type = type is PointerTypeExpression p ? p.Inner : ((ManagedTypeExpression)type).Inner;
        if (type is not NamedTypeExpression named) return null;
        string name = named.Namespace == null ? named.Name : Join(named.Namespace, named.Name);
        name = name.Split('$')[0]; // Source template, rather than a lowered specialization.
        return symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == name)?.Qualified
            ?? Visible(path, offset).FirstOrDefault(s => IsType(s.Syntax.Node) && s.Name == name)?.Qualified;
    }
    private IEnumerable<Symbol> TypeMembers(string owner)
    {
        var visited = new HashSet<string>();
        while (visited.Add(owner))
        {
            foreach (var symbol in symbols.Where(s => s.Owner == owner && s.Scope == null)) yield return symbol;
            var type = symbols.FirstOrDefault(s => s.Qualified == owner && s.Syntax.Node is ClassDeclaration);
            if (type?.Syntax.Node is not ClassDeclaration c) yield break;
            // The parser records the colon list in Interfaces; checking separates
            // its base class into ClassInfo without rewriting the declaration.
            string? baseClass = snapshot.Checker?.GetClass(owner)?.BaseClass ?? c.BaseClass ??
                c.Interfaces.FirstOrDefault(name => symbols.Any(s => s.Syntax.Node is ClassDeclaration && (s.Qualified == name || s.Qualified == Join(type.Namespace, name))));
            if (baseClass == null) yield break;
            owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && (s.Qualified == baseClass || s.Qualified == Join(type.Namespace, baseClass)))?.Qualified ?? baseClass;
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

    private Symbol? ResolveCore(string path, int index)
    {
        var token = tokens[path][index];
        var declaration = symbols.FirstOrDefault(s => s.NameSpan.Source?.Path == path && s.NameSpan.Start == token.Start && s.NameSpan.Length == token.Span.Length);
        if (declaration != null) return declaration;
        var call = nodes[path].Where(n => n.Node is CallExpression c && c.Callee.Span.Start <= token.Start && c.Callee.Span.Start + c.Callee.Span.Length == token.End + 1).MinBy(n => n.Span.Length);
        if (call?.Node is CallExpression expression && snapshot.Checker?.GetResolvedCall(expression) is AstNode target && declarations.TryGetValue(target, out var method)) return method;
        var candidates = Candidates(path, token.Start, index);
        // A local named `String` must not recolor a `String` type annotation.
        if (nodes[path].Any(n => n.Node is NamedTypeExpression && n.Span.Length > 0 && token.Start >= n.Span.Start && token.End < n.Span.Start + n.Span.Length))
            candidates = candidates.Where(s => IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration);
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
            .DistinctBy(s => s.Name).Select(s => (object)new { label = s.Name, kind = s.Kind, detail = Detail(s), textEdit = new { range, newText = s.Name } }).ToList();
        if (!member && !completionContext.Restricted)
        {
            bool inBody = ContextParents(path, offset).Any(p => p.Node is BlockStatement);
            string[] keywords = inBody ? ["if", "else", "for", "foreach", "while", "return", "new", "delete", "defer", "try", "catch", "throw", "this", "null", "true", "false", "sizeof", "alignof", "nameof"] : ["namespace", "using", "class", "struct", "interface", "enum", "public", "private", "static", "extern", "readonly", "const", "weak", "replace"];
            foreach (var word in keywords.Concat(new[] { "void", "bool", "char", "int", "uint", "long", "ulong", "nint", "nuint", "float", "double", "readonly" }).Distinct())
                items.Add(new { label = word, kind = 14, textEdit = new { range, newText = word } });
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
    public object? Hover(string path, int offset, VisualStudioHover? presentation = null)
    {
        var compound = CompoundTypeHover(path, offset, presentation);
        if (compound != null) return compound;
        int index = TokenAt(path, offset);
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
                    string name = DisplayType(literalType);
                    var target = TypeSymbol(name);
                    return new HoverResult(new("markdown", "```gflat\n" + name + "\n```"), Workspace.ToRange(literal.Span),
                        presentation?.Render([new(TypeChecker.IsPrimitive(name) ? "keyword" : "type", name, Target: SymbolLocation(target))], [], "constant.public"));
                }
            }
        }
        if (symbol == null && index >= 0 && TypeChecker.IsPrimitive(tokens[path][index].Text) && (tokens[path][index].Kind != TokenKind.Identifier || nodes[path].Any(n => n.Node is NamedTypeExpression && Contains(n, offset))))
        {
            string name = tokens[path][index].Text;
            var type = new NamedTypeExpression(name, null, 0);
            HoverDetail[] layout = [];
            if (snapshot.Checker is { } checker)
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
        HoverDetail[] details = LayoutDetails(symbol);
        if (symbol.Syntax.Node is MethodDeclaration hoveredMethod && snapshot.Checker is { } exceptionChecker &&
            !snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            var exceptions = exceptionChecker.GetPossibleExceptions(hoveredMethod);
            if (exceptions.Count > 0)
            {
                details = [.. details, new("", "Exceptions:")];
                foreach (string exception in exceptions)
                {
                    bool unknown = exception == TypeChecker.UnknownExceptionType;
                    string name = unknown ? "Unknown exceptions" : exceptionChecker.DisplayName(exception);
                    details = [.. details, new("", "  " + name, [new("text", "  "), new(unknown ? "text" : "class", name, Target: unknown ? null : SymbolLocation(TypeSymbol(name, symbol)))])];
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
    public object? SignatureHelp(string path, int offset)
    {
        var ts = tokens[path];
        var stack = new Stack<(int Index, int Commas)>();
        for (int i = 0; i < ts.Length && ts[i].Start < offset; i++)
        {
            if (ts[i].Kind is TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace) stack.Push((i, 0));
            else if (ts[i].Kind is TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace) { if (stack.Count > 0) stack.Pop(); }
            else if (ts[i].Kind == TokenKind.Comma && stack.Count > 0) { var frame = stack.Pop(); stack.Push((frame.Index, frame.Commas + 1)); }
        }
        var call = stack.FirstOrDefault(f => ts[f.Index].Kind == TokenKind.OpenParen && f.Index > 0 && ts[f.Index - 1].Kind == TokenKind.Identifier, (Index: -1, Commas: 0));
        if (call.Index < 0) return null;
        int nameIndex = call.Index - 1;
        var candidates = Candidates(path, offset, nameIndex).Where(s => s.Name == ts[nameIndex].Text && s.Parameters != null).ToArray();
        if (candidates.Length == 0) return null;
        var selected = Resolve(path, nameIndex);
        int active = Math.Max(0, Array.IndexOf(candidates, selected));
        return new { signatures = candidates.Select(s => new { label = Detail(s), parameters = s.Parameters!.Select(p => new { label = ParameterText(p) }).ToArray() }).ToArray(), activeSignature = active, activeParameter = call.Commas };
    }
    public object SemanticTokens(string path, TextRange? range = null)
    {
        var data = new List<int>();
        int previousLine = 0, previousColumn = 0;
        var ts = tokens[path];
        for (int i = 0; i < ts.Length; i++)
        {
            bool control = ts[i].Kind is TokenKind.If or TokenKind.Else or TokenKind.For or TokenKind.Foreach or TokenKind.While or TokenKind.Return or TokenKind.Break or TokenKind.Continue or TokenKind.Try or TokenKind.Catch or TokenKind.Throw or TokenKind.Defer;
            bool modifier = ts[i].Kind == TokenKind.Throws;
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
