using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private Symbol? TypeSymbol(string name, Symbol? context = null)
    {
        if (context != null && new[] { context.Syntax }.Concat(Parents(context.Syntax)).Any(n => n.Node switch
        {
            MethodDeclaration m => m.GenericParameters.Any(p => p.Name == name),
            ClassDeclaration c => c.GenericParameters.Any(p => p.Name == name),
            StructDeclaration s => s.GenericParameters.Any(p => p.Name == name),
            _ => false
        })) return null;
        var types = symbols.Where(s => IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration).ToArray();
        var exact = types.FirstOrDefault(s => s.Qualified == name);
        if (exact != null) return exact;
        if (context?.NameSpan.Source is { } source)
            return Visible(source.Path, context.NameSpan.Start).FirstOrDefault(s => (IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration) && s.Name == name);
        return null;
    }

    private static Location? SymbolLocation(Symbol? symbol) => symbol?.NameSpan.Source is not SourceFile source || source.Path.StartsWith('<')
        ? null : new(Workspace.FileUri(source.Path), Workspace.ToRange(symbol.NameSpan));

    private string DisplayType(TypeExpression type) => snapshot.Checker?.DisplayTypeName(type) ?? TypeChecker.TypeName(type).Replace(".", "::");
    private string Detail(Symbol symbol) => string.Concat(Signature(symbol).Select(p => p.Text));

    private DisplayPart[] Signature(Symbol symbol, bool navigable = false, string? hoverPath = null, int hoverOffset = 0)
    {
        var parts = new List<DisplayPart>();
        void Add(string text, string kind = "text", Location? target = null) { if (text.Length > 0) parts.Add(new(kind, text, Target: target)); }
        void Keyword(string word) { Add(word, "keyword"); Add(" "); }
        void Type(TypeExpression type)
        {
            string text = ShortType(DisplayType(type), hoverPath, hoverOffset);
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
            int cursor = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                Add(text[cursor..token.Start]);
                string kind = TypeChecker.IsPrimitive(token.Text) ? "keyword" : token.Kind == TokenKind.Identifier
                    ? i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.DoubleColon ? "namespace" : "type"
                    : char.IsLetter(token.Text.FirstOrDefault()) ? "keyword" : char.IsDigit(token.Text.FirstOrDefault()) ? "number" : "punctuation";
                Location? target = null;
                if (navigable && token.Kind == TokenKind.Identifier)
                {
                    int first = i;
                    while (first >= 2 && tokens[first - 1].Kind == TokenKind.DoubleColon) first -= 2;
                    string name = string.Concat(tokens[first..(i + 1)].Select(t => t.Text));
                    var declaration = TypeSymbol(name, symbol);
                    target = SymbolLocation(declaration);
                    if (declaration != null) kind = declaration.Classification;
                }
                Add(text[token.Start..(token.End + 1)], kind, target);
                cursor = token.End + 1;
            }
            Add(text[cursor..]);
        }
        void Name()
        {
            string name = symbol.Qualified;
            if (hoverPath != null && symbol.Scope == null && symbol.Owner != null && !IsType(symbol.Syntax.Node))
            {
                string owner = ShortType(symbol.Owner, hoverPath, hoverOffset);
                // An instance member uses the same separator as an instance access.
                var ownerSymbol = TypeSymbol(symbol.Owner, symbol);
                Add(owner, ownerSymbol?.Classification ?? "type", navigable ? SymbolLocation(ownerSymbol) : null);
                Add(symbol.Static ? "::" : ".", "punctuation");
                Add(symbol.Name, symbol.Classification, navigable ? SymbolLocation(symbol) : null);
            }
            else
            {
                var names = name.Split("::");
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) Add("::", "punctuation");
                    var target = navigable ? (i == names.Length - 1 ? symbol : TypeSymbol(string.Join("::", names.Take(i + 1)), symbol)) : null;
                    Add(names[i], i == names.Length - 1 ? symbol.Classification : target?.Classification ?? "namespace", SymbolLocation(target));
                }
            }
            var generics = symbol.Syntax.Node switch { MethodDeclaration m => m.GenericParameters, ClassDeclaration c => c.GenericParameters, StructDeclaration s => s.GenericParameters, _ => null };
            if (generics?.Count > 0)
            {
                Add("<", "punctuation");
                for (int i = 0; i < generics.Count; i++)
                {
                    if (i > 0) Add(", ", "punctuation");
                    Add(generics[i].Name, "type");
                }
                Add(">", "punctuation");
            }
        }
        if (symbol.Scope == null && symbol.Syntax.Node is not NamespaceDeclaration and not ExternDeclaration and not EnumMemberDeclaration)
        {
            string? access = symbol.Access switch { TokenKind.Public => "public", TokenKind.Private => "private", TokenKind.Protected => "protected", TokenKind.Internal => "internal", _ => null };
            if (access != null) Keyword(access);
        }
        if (symbol.Syntax.Node is MethodDeclaration method)
        {
            if (method.IsStatic) Keyword("static");
            if (method.IsReadOnly) Keyword("readonly");
            if (method.IsConst) Keyword("const");
            if (method.IsAbstract) Keyword("abstract");
            if (method.IsVirtual) Keyword("virtual");
            if (method.IsOverride) Keyword("override");
            if (method.ImplementationKind != FunctionImplementationKind.Ordinary) Keyword(method.ImplementationKind.ToString().ToLowerInvariant());
        }
        else if (symbol.Syntax.Node is FieldDeclaration field)
        {
            if (field.IsStatic) Keyword("static");
            if (field.IsConst) Keyword("const");
            if (field.IsReadOnly) Keyword("readonly");
        }
        else if (symbol.Syntax.Node is ExternDeclaration) Keyword("extern");
        else if (symbol.Syntax.Node is VariableDeclaration { IsConst: true } or Parameter { IsConst: true }) Keyword("const");
        else if (symbol.Syntax.Node is PropertyDeclaration && symbol.Static) Keyword("static");

        if (symbol.Syntax.Node is AliasDeclaration alias)
        {
            Keyword("alias"); Name(); Add(" = ", "punctuation"); Type(alias.TargetType);
        }
        else
        {
            if (IsType(symbol.Syntax.Node) || symbol.Syntax.Node is NamespaceDeclaration) Keyword(symbol.Classification);
            else if (symbol.Type != null) { Type(symbol.Type); Add(" "); }
            Name();
        }
        if (symbol.Parameters is { } parameters)
        {
            Add("(", "punctuation");
            for (int i = 0; i < parameters.Count; i++)
            {
                if (i > 0) Add(", ", "punctuation");
                var parameter = parameters[i];
                if (parameter.IsConst) Keyword("const");
                Type(parameter.Type); Add(" "); Add(parameter.Name, "parameter", navigable ? SymbolLocation(declarations.GetValueOrDefault(parameter)) : null);
            }
            if (symbol.Syntax.Node is ExternDeclaration { IsVariadic: true }) Add(parameters.Count == 0 ? "..." : ", ...", "punctuation");
            Add(")", "punctuation");
            if (symbol.Syntax.Node is MethodDeclaration { Throws: true }) { Add(" "); Add("throws", "keyword"); }
        }
        if (symbol.Syntax.Node is PropertyDeclaration property)
        {
            Add(" { ", "punctuation");
            if (property.Getter != null) { Add("get", "keyword"); Add("; ", "punctuation"); }
            if (property.Setter != null)
            {
                if (property.Setter.Accessibility != symbol.Access) Keyword(property.Setter.Accessibility.ToString().ToLowerInvariant());
                Add("set", "keyword"); Add("; ", "punctuation");
            }
            Add("}", "punctuation");
        }
        return parts.ToArray();
    }

    private HoverDetail[] LayoutDetails(Symbol symbol)
    {
        // Do not query incomplete or open layouts: the checker may not have
        // visited later declarations after an error, or specialized a template.
        if ((!IsType(symbol.Syntax.Node) && symbol.Syntax.Node is not AliasDeclaration) ||
            snapshot.Checker is not { } checker || snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ||
            symbol.Syntax.Node is ClassDeclaration { IsGeneric: true } or StructDeclaration { IsGeneric: true }) return [];
        TypeExpression? type = IsType(symbol.Syntax.Node) ? new NamedTypeExpression(symbol.Qualified, null, 0) : symbol.Type;
        if (type == null) return [];
        var recorded = checker.GetType(symbol.Syntax.Node);
        if (TypeChecker.TypeName(recorded) != "<error>") type = recorded;
        return TypeLayoutDetails(type);
    }

    private HoverDetail[] TypeLayoutDetails(TypeExpression type)
    {
        if (snapshot.Checker is not { } checker || snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return [];
        try
        {
            // Resolve aliases and validate before querying; unknown alignment has
            // historically had a fallback value intended for internal use only.
            type = checker.ResolveAlias(type);
            if (!Known(type)) return [];
            int size = checker.GetTypeSize(type), alignment = checker.GetTypeAlignment(type);
            string label = type is NamedTypeExpression named && checker.GetClass(named.Name) != null ? "Object size" : "Size";
            return [new(label, $"{size} bytes"), new("Alignment", $"{alignment} bytes")];
        }
        catch (TypeCheckException) { return []; }

        bool Known(TypeExpression candidate) => candidate switch
        {
            PointerTypeExpression p => Known(p.Inner), ManagedTypeExpression p => Known(p.Inner),
            FunctionPointerTypeExpression => true,
            ArrayTypeExpression a => a.Size != null && Known(a.ElementType),
            NamedTypeExpression n => n.TypeArguments.Count == 0 && (TypeChecker.IsPrimitive(n.Name) || checker.GetClass(n.Name) != null || checker.GetStruct(n.Name) != null || checker.GetInterface(n.Name) != null || checker.ResolveEnum(n) != null),
            _ => false
        };
    }

    // Shorten only if the visible type name uniquely denotes this declaration.
    // Use the request location: imports at the declaration can differ from the caller's.
    private string ShortType(string text, string? path, int offset)
    {
        if (path == null) return text;
        var visible = Visible(path, offset).Where(s => IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration).ToArray();
        var typeParameters = ContextParents(path, offset).SelectMany(n => n.Node switch
        {
            MethodDeclaration m => m.GenericParameters.Select(p => p.Name),
            ClassDeclaration c => c.GenericParameters.Select(p => p.Name),
            StructDeclaration st => st.GenericParameters.Select(p => p.Name),
            _ => Enumerable.Empty<string>()
        }).ToHashSet();
        return System.Text.RegularExpressions.Regex.Replace(text, @"\b[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)+", match =>
        {
            string simple = match.Value.Split("::").Last();
            var matches = visible.Where(s => s.Name == simple).DistinctBy(s => s.Qualified).ToArray();
            return !typeParameters.Contains(simple) && matches.Length == 1 && matches[0].Qualified == match.Value ? simple : match.Value;
        });
    }

    private object? CompoundTypeHover(string path, int offset, VisualStudioHover? presentation)
    {
        var ts = tokens[path];
        int index = TokenAt(path, offset);
        if (index < 0 || ts[index].Kind == TokenKind.Identifier || TypeChecker.IsPrimitive(ts[index].Text)) return null;
        var syntax = nodes[path].Where(n => n.Node is TypeExpression && Contains(n, offset))
            .OrderByDescending(n => n.Span.Length).FirstOrDefault();
        if (syntax?.Node is not TypeExpression type || type is NamedTypeExpression && !type.IsReadOnlyValue) return null;
        string text = ShortType(DisplayType(type), path, offset);
        var parts = new List<DisplayPart>();
        var lexed = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        int cursor = 0;
        var context = symbols.FirstOrDefault(s => s.Syntax == syntax.Parent);
        for (int i = 0; i < lexed.Length; i++)
        {
            var token = lexed[i];
            if (token.Start > cursor) parts.Add(new("text", text[cursor..token.Start]));
            int first = i;
            while (first >= 2 && lexed[first - 1].Kind == TokenKind.DoubleColon) first -= 2;
            var target = token.Kind == TokenKind.Identifier ? TypeSymbol(string.Concat(lexed[first..(i + 1)].Select(t => t.Text)), context) : null;
            parts.Add(new(target?.Classification ?? (TypeChecker.IsPrimitive(token.Text) || token.Kind == TokenKind.Readonly ? "keyword" : token.Kind == TokenKind.Identifier ? "type" : "punctuation"), token.Text, Target: SymbolLocation(target)));
            cursor = token.End + 1;
        }
        var layout = TypeLayoutDetails(type);
        return new HoverResult(new("markdown", "```gflat\n" + text + "\n```" + (layout.Length == 0 ? "" : "\n\n" + string.Join("  \n", layout.Select(d => d.Markdown)))),
            Workspace.ToRange(syntax.Span), presentation?.Render(parts.ToArray(), layout, "type.public"));
    }

    private static string Glyph(Symbol symbol)
    {
        string kind = symbol.Syntax.Node switch
        {
            FieldDeclaration { IsConst: true } or VariableDeclaration { IsConst: true } => "constant",
            FieldDeclaration => "field", AliasDeclaration => "type", _ => symbol.Classification
        };
        string access = symbol.Access switch { TokenKind.Private => "private", TokenKind.Protected => "protected", TokenKind.Internal => "internal", _ => "public" };
        return kind + "." + access;
    }
}
