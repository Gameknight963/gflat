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
            InterfaceDeclaration i => i.GenericParameters.Any(p => p.Name == name),
            ExtensionDeclaration e => e.GenericParameters.Any(p => p.Name == name),
            _ => false
        })) return symbols.FirstOrDefault(s => s.IsTypeParameter && s.Name == name &&
            s.Scope != null && (s.Scope == context.Syntax || Parents(context.Syntax).Contains(s.Scope)));
        int separator = name.LastIndexOf("::", StringComparison.Ordinal);
        string simpleName = separator < 0 ? name : name[(separator + 2)..];
        var types = SymbolsNamed(simpleName).Where(s => !s.IsTypeParameter && (IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration));
        if (!name.Contains("::", StringComparison.Ordinal) && context?.NameSpan.Source is { } declaringSource)
        {
            var visible = Visible(declaringSource.Path, context.NameSpan.Start).FirstOrDefault(s =>
                (IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration) && s.Name == name);
            if (visible != null) return visible;
        }
        var exact = types.FirstOrDefault(s => s.Scope == null && s.Qualified == name);
        if (exact != null) return exact;
        if (context?.NameSpan.Source is { } source)
            return Visible(source.Path, context.NameSpan.Start).FirstOrDefault(s => (IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration) && s.Name == name);
        return null;
    }

    private static Location? SymbolLocation(Symbol? symbol) => symbol?.NameSpan.Source is not SourceFile source || source.Path.StartsWith('<')
        ? null : new(Workspace.FileUri(source.Path), Workspace.ToRange(symbol.NameSpan));

    private string DisplayType(TypeExpression type) => snapshot.Checker?.DisplayTypeName(type) ?? TypeChecker.TypeName(type).Replace(".", "::");
    private string SourceTypeSpelling(TypeExpression type, Symbol context)
    {
        string text = DisplayType(type);
        if (TypeChecker.IsPrimitive(text)) return text;
        var parts = new List<DisplayPart>();
        AddTypeDisplayParts(parts, text, context, sourceSpelling: true);
        return string.Concat(parts.Select(part => part.Text));
    }
    private string Detail(Symbol symbol) => string.Concat(Signature(symbol).Select(p => p.Text));
    private readonly HashSet<string> escapedTypeDisplayNames = new(StringComparer.Ordinal);

    private static TypeExpression ArrayDisplayShape(TypeExpression declared, TypeExpression actual)
    {
        if (declared is not ArrayTypeExpression syntax || actual is not ArrayTypeExpression checkedArray)
            return declared;
        return new ArrayTypeExpression(ArrayDisplayShape(syntax.ElementType, checkedArray.ElementType),
            checkedArray.Size ?? syntax.Size, syntax.Line, syntax.SizeExpression) { Span = syntax.Span }
            .WithQualifiersFrom(syntax);
    }

    private void AddTypeDisplayParts(List<DisplayPart> parts, string text, Symbol context, bool navigable = false,
        Symbol? finalType = null, IReadOnlyDictionary<int, Symbol>? shortened = null, bool sourceSpelling = false)
    {
        void Add(string value, string kind = "text", Location? target = null) { if (value.Length > 0) parts.Add(new(kind, value, Target: target)); }
        var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        int finalTypeName = -1;
        if (finalType != null)
        {
            int depth = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Kind == TokenKind.Less) depth++;
                else if (tokens[i].Kind == TokenKind.Greater) depth--;
                else if (tokens[i].Kind == TokenKind.GreaterGreater) depth -= 2;
                else if (depth == 0 && tokens[i].Kind == TokenKind.Identifier) finalTypeName = i;
            }
        }
        int cursor = 0;
        bool inSplice = false;
        for (int i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Kind == TokenKind.OpenBracket && i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.Colon) inSplice = true;
            Add(text[cursor..token.Start]);
            string kind = (TypeChecker.IsPrimitive(token.Text) || token.Text == "ref") ? "keyword" : token.Kind == TokenKind.Identifier
                ? i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.DoubleColon ? "namespace" : "type"
                : char.IsLetter(token.Text.FirstOrDefault()) ? "keyword" : char.IsDigit(token.Text.FirstOrDefault()) ? "number" : "punctuation";
            Location? target = null;
            string spelling = text[token.Start..(token.End + 1)];
            bool qualifiedName = i > 0 && tokens[i - 1].Kind == TokenKind.DoubleColon;
            bool shortenedName = shortened?.ContainsKey(token.Start) == true;
            bool escapedTypeKeyword = token.Kind != TokenKind.Identifier && char.IsLetter(token.Text.FirstOrDefault()) &&
                (qualifiedName || shortenedName ||
                    !TypeChecker.IsPrimitive(token.Text) && token.Kind != TokenKind.Ref &&
                    !(i + 1 < tokens.Length &&
                        (token.Kind is TokenKind.Readonly or TokenKind.Immutable && tokens[i + 1].Kind == TokenKind.OpenParen ||
                         token.Kind == TokenKind.Managed && tokens[i + 1].Kind == TokenKind.Less)));
            if (inSplice && token.Kind == TokenKind.Identifier)
                kind = token.Text == "this" ? "keyword" : i > 0 && tokens[i - 1].Kind == TokenKind.Dot ? "property" : "variable";
            bool sourceEscape = !navigable && !sourceSpelling && token.Kind == TokenKind.Identifier && escapedTypeDisplayNames.Contains(token.Text);
            if (!inSplice && ((navigable || sourceSpelling || sourceEscape) && token.Kind == TokenKind.Identifier || escapedTypeKeyword))
            {
                int first = i;
                while (first >= 2 && tokens[first - 1].Kind == TokenKind.DoubleColon) first -= 2;
                string name = string.Concat(tokens[first..(i + 1)].Select(t => t.Text));
                // Only names actually shortened at this caller location
                // use its proven unique declaration. Untouched aliases and
                // generic parameters retain their declaring-source identity.
                var declaration = shortened?.GetValueOrDefault(token.Start) ?? TypeSymbol(name, context);
                target = navigable ? SymbolLocation(declaration) : null;
                if (declaration != null)
                {
                    kind = declaration.Classification;
                    spelling = CompletionSpelling(declaration);
                }
            }
            // `>::Nested` is not an ordinary qualified-name token chain.
            // Its checked source symbol is already known; do not recover it
            // from the display spelling or mistake a generic argument for it.
            if (navigable && i == finalTypeName && finalType != null)
            {
                kind = finalType.Classification;
                target = SymbolLocation(finalType);
            }
            Add(spelling, kind, target);
            if (token.Kind == TokenKind.CloseBracket && i > 0 && tokens[i - 1].Kind == TokenKind.Colon) inSplice = false;
            cursor = token.End + 1;
        }
        Add(text[cursor..]);
    }

    private DisplayPart[] Signature(Symbol symbol, bool navigable = false, string? hoverPath = null, int hoverOffset = 0)
    {
        var parts = new List<DisplayPart>();
        void Add(string text, string kind = "text", Location? target = null) { if (text.Length > 0) parts.Add(new(kind, text, Target: target)); }
        void Keyword(string word) { Add(word, "keyword"); Add(" "); }
        void Type(TypeExpression type) => ShortTypeText(DisplayType(type));
        void ShortTypeText(string original, Symbol? finalType = null)
        {
            if (original.Contains("[:", StringComparison.Ordinal)) { TypeText(original); return; }
            Dictionary<int, Symbol>? shortened = navigable ? new() : null;
            string text = ShortType(original, hoverPath, hoverOffset, shortened);
            TypeText(text, finalType, shortened);
        }
        void TypeText(string text, Symbol? finalType = null, IReadOnlyDictionary<int, Symbol>? shortened = null)
            => AddTypeDisplayParts(parts, text, symbol, navigable, finalType, shortened);
        void Name()
        {
            // A nested type use retains its checked enclosing specialization.
            // Keep the original symbol for source navigation and glyph identity.
            if (IsType(symbol.Syntax.Node) && symbol.BoundType is { } boundName)
            {
                ShortTypeText(DisplayType(boundName), symbol);
                return;
            }
            string name = symbol.Qualified;
            if (hoverPath != null && symbol.Scope == null && symbol.Owner != null && !IsType(symbol.Syntax.Node))
            {
                // An instance member uses the same separator as an instance access.
                var ownerSymbol = TypeSymbol(symbol.Owner, symbol);
                string owner = ownerSymbol?.Namespace is { Length: > 0 } ns && symbol.Owner.StartsWith(ns + "::", StringComparison.Ordinal)
                    ? symbol.Owner[(ns.Length + 2)..] : symbol.Owner;
                TypeText(owner);
                Add(symbol.Static ? "::" : ".", "punctuation");
                Add(CompletionSpelling(symbol), symbol.Classification, navigable ? SymbolLocation(symbol) : null);
            }
            else
            {
                var names = name.Split("::");
                for (int i = 0; i < names.Length; i++)
                {
                    if (i > 0) Add("::", "punctuation");
                    var target = navigable ? (i == names.Length - 1 ? symbol : TypeSymbol(string.Join("::", names.Take(i + 1)), symbol)) : null;
                    string spelling = i == names.Length - 1 ? CompletionSpelling(symbol) :
                        Lexer.GetKeyword(names[i]) != TokenKind.Identifier ? "@" + names[i] : names[i];
                    Add(spelling, i == names.Length - 1 ? symbol.Classification : target?.Classification ?? "namespace", SymbolLocation(target));
                }
            }
            var generics = symbol.Syntax.Node switch { MethodDeclaration m => m.GenericParameters, ClassDeclaration c => c.GenericParameters, StructDeclaration s => s.GenericParameters, InterfaceDeclaration i => i.GenericParameters, _ => null };
            if (generics?.Count > 0)
            {
                Add("<", "punctuation");
                for (int i = 0; i < generics.Count; i++)
                {
                    if (i > 0) Add(", ", "punctuation");
                    if (symbol.GenericBindings?.TryGetValue(generics[i].Name, out var binding) == true) Type(binding);
                    else
                    {
                        var parameter = TypeSymbol(generics[i].Name, symbol);
                        string parameterName = parameter?.IsTypeParameter == true ? CompletionSpelling(parameter) :
                            Lexer.GetKeyword(generics[i].Name) != TokenKind.Identifier ? "@" + generics[i].Name : generics[i].Name;
                        Add(parameterName, "typeParameter", navigable ? SymbolLocation(parameter) : null);
                    }
                }
                Add(">", "punctuation");
            }
        }
        if (symbol.IsTypeParameter)
            return [new("text", "type parameter "), new("typeParameter", symbol.Name, Target: navigable ? SymbolLocation(symbol) : null)];
        if (symbol.Scope == null && symbol.Syntax.Node is not NamespaceDeclaration and not EnumMemberDeclaration and not MethodDeclaration { StaticConstructorName: not null })
        {
            string? access = symbol.Access switch { TokenKind.Public => "public", TokenKind.Private => "private", TokenKind.Protected => "protected", TokenKind.Internal => "internal", _ => null };
            if (access != null) Keyword(access);
        }
        if (symbol.Syntax.Node.IsInline) Keyword("inline");
        if (symbol.Syntax.Node is ClassDeclaration { IsStatic: true } or InterfaceDeclaration { IsStatic: true }) Keyword("static");
        if (symbol.Syntax.Node is MethodDeclaration method)
        {
            if (method.IsStatic) Keyword("static");
            if (method.IsImmutable) Keyword("immutable");
            else if (method.IsReadOnly) Keyword("readonly");
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
            // readonly(T) already expresses a readonly field. A method's
            // readonly receiver remains a separate modifier above.
            if (field.IsReadOnly && !field.Type.IsReadOnlyValue) Keyword("readonly");
        }
        else if (symbol.Syntax.Node is ExternDeclaration external)
        {
            Keyword("extern");
            if (external.IsCLinkage) { Add("\"C\"", "string"); Add(" "); }
        }
        else if (symbol.Syntax.Node is VariableDeclaration { IsConst: true } or Parameter { IsConst: true }) Keyword("const");
        else if (symbol.Syntax.Node is PropertyDeclaration && symbol.Static) Keyword("static");

        if (symbol.Syntax.Node is PropertyDeclaration { IsRef: true }) Keyword("ref");
        if (symbol.Syntax.Node is ConstructorDeclaration { IsCopy: true }) Keyword("copy");
        if (symbol.Syntax.Node is ConstructorDeclaration { IsMove: true }) Keyword("move");

        if (symbol.Syntax.Node is AliasDeclaration alias)
        {
            if (alias.ImplementationKind != FunctionImplementationKind.Ordinary) Keyword(alias.ImplementationKind.ToString().ToLowerInvariant());
            if (alias.IsConstrained) Keyword("constrained");
            Keyword("alias"); Name();
            if (alias.Constraints.Count > 0)
            {
                Add(" : ", "punctuation");
                for (int i = 0; i < alias.Constraints.Count; i++) { if (i > 0) Add(", ", "punctuation"); Type(alias.Constraints[i]); }
            }
            if (alias.TargetType != null) { Add(" = ", "punctuation"); Type(alias.TargetType); }
        }
        else
        {
            if (IsType(symbol.Syntax.Node) || symbol.Syntax.Node is NamespaceDeclaration) Keyword(symbol.Classification);
            else if (symbol.Type != null)
            {
                var display = symbol.BoundType ?? (symbol.BoundParameters != null ? symbol.Type : snapshot.DeclaredTypes.GetValueOrDefault(symbol.Syntax.Node) ?? symbol.Type);
                if (symbol.Syntax.Node is VariableDeclaration && snapshot.Checker is { } checker)
                    display = ArrayDisplayShape(display, checker.GetType(symbol.Syntax.Node));
                Type(display); Add(" ");
            }
            Name();
        }
        if (symbol.Parameters is { } parameters)
        {
            Add("(", "punctuation");
            for (int i = 0; i < parameters.Count; i++)
            {
                if (i > 0) Add(", ", "punctuation");
                var parameter = parameters[i];
                if (parameter.IsExtensionReceiver) Keyword("this");
                if (parameter.IsConst) Keyword("const");
                var originalParameter = symbol.BoundParameters != null && symbol.Syntax.Node is MethodDeclaration sourceMethod
                    ? sourceMethod.Parameters.ElementAtOrDefault(i + (symbol.BoundExtension && sourceMethod.Parameters.FirstOrDefault()?.IsExtensionReceiver == true ? 1 : 0)) : parameter;
                var parameterSymbol = originalParameter == null ? null : declarations.GetValueOrDefault(originalParameter);
                string parameterName = parameterSymbol != null ? CompletionSpelling(parameterSymbol) :
                    Lexer.GetKeyword(parameter.Name) != TokenKind.Identifier ? "@" + parameter.Name : parameter.Name;
                Type(symbol.BoundParameters != null ? parameter.Type : snapshot.DeclaredTypes.GetValueOrDefault(parameter) ?? parameter.Type);
                Add(" ");
                Add(parameterName, "parameter", navigable ? SymbolLocation(parameterSymbol) : null);
                if (parameter.DefaultValue != null)
                {
                    Add(" = ", "punctuation");
                    parts.AddRange(ParameterDefaultParts(parameter, navigable));
                }
            }
            if (symbol.Syntax.Node is ExternDeclaration { IsVariadic: true }) Add(parameters.Count == 0 ? "..." : ", ...", "punctuation");
            Add(")", "punctuation");
            if (symbol.Syntax.Node is MethodDeclaration { IsNoThrow: true } or ConstructorDeclaration { IsNoThrow: true } or ExternDeclaration { IsNoThrow: true }) { Add(" "); Add("nothrow", "keyword"); }
        }
        if (symbol.Syntax.Node is PropertyDeclaration property)
        {
            Add(" { ", "punctuation");
            if (property.Getter != null)
            {
                if (property.Getter.Accessibility != symbol.Access) Keyword(property.Getter.Accessibility.ToString().ToLowerInvariant());
                if (property.Getter.IsImmutable) Keyword("immutable");
                else if (property.Getter.IsReadOnly) Keyword("readonly");
                Add("get", "keyword");
                if (property.Getter.IsNoThrow) { Add(" "); Add("nothrow", "keyword"); }
                Add("; ", "punctuation");
            }
            if (property.Setter != null)
            {
                if (property.Setter.Accessibility != symbol.Access) Keyword(property.Setter.Accessibility.ToString().ToLowerInvariant());
                if (property.Setter.IsImmutable) Keyword("immutable");
                else if (property.Setter.IsReadOnly) Keyword("readonly");
                Add("set", "keyword");
                if (property.Setter.IsNoThrow) { Add(" "); Add("nothrow", "keyword"); }
                Add("; ", "punctuation");
            }
            Add("}", "punctuation");
        }
        return parts.ToArray();
    }

    private static string ParameterDefaultText(Parameter parameter)
    {
        var value = parameter.DefaultValue!;
        return value.Span.Source is { } source && value.Span.Length > 0
            ? source.Text.Substring(value.Span.Start, value.Span.Length)
            : value is LiteralExpression literal ? literal.Token.Text : "default";
    }

    private IEnumerable<DisplayPart> ParameterDefaultParts(Parameter parameter, bool navigable)
    {
        string text = ParameterDefaultText(parameter);
        int cursor = 0;
        foreach (var token in Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile))
        {
            if (token.Start > cursor) yield return new("text", text[cursor..token.Start]);
            Symbol? symbol = null;
            if (parameter.DefaultValue!.Span.Source is { } source && tokens.TryGetValue(source.Path, out var original))
            {
                int index = Array.FindIndex(original, t => t.Start == parameter.DefaultValue.Span.Start + token.Start);
                if (index >= 0) symbol = Resolve(source.Path, index);
            }
            string kind = symbol?.Classification ?? (token.Kind is TokenKind.StringLiteral or TokenKind.CharLiteral ? "string" :
                char.IsDigit(token.Text.FirstOrDefault()) ? "number" : token.Kind == TokenKind.Identifier ? "identifier" :
                char.IsLetter(token.Text.FirstOrDefault()) ? "keyword" : "punctuation");
            yield return new(kind, text[token.Start..(token.End + 1)], Target: navigable ? SymbolLocation(symbol) : null);
            cursor = token.End + 1;
        }
        if (cursor < text.Length) yield return new("text", text[cursor..]);
    }

    private HoverDetail[] LayoutDetails(Symbol symbol)
    {
        // Do not query incomplete or open layouts: the checker may not have
        // visited later declarations after an error, or specialized a template.
        if ((!IsType(symbol.Syntax.Node) && symbol.Syntax.Node is not AliasDeclaration) ||
            snapshot.Checker is not { } checker || snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ||
            symbol.Syntax.Node is ClassDeclaration { IsStatic: true } ||
            symbol.BoundType == null && (symbol.Syntax.Node is ClassDeclaration { IsGeneric: true } or StructDeclaration { IsGeneric: true })) return [];
        TypeExpression? type = symbol.BoundType ?? (IsType(symbol.Syntax.Node) ? new NamedTypeExpression(symbol.Qualified, null, 0) : symbol.Type);
        if (type == null) return [];
        var recorded = checker.GetType(symbol.Syntax.Node);
        if (symbol.BoundType == null && TypeChecker.TypeName(recorded) != "<error>") type = recorded;
        return TypeLayoutDetails(type);
    }

    private HoverDetail[] TypeLayoutDetails(TypeExpression type)
    {
        if (type is ReferenceTypeExpression) return [];
        if (snapshot.Checker is not { } checker || snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)) return [];
        try
        {
            // Resolve aliases and validate before querying; unknown alignment has
            // historically had a fallback value intended for internal use only.
            type = checker.ResolveAlias(type);
            if (!Known(type)) return [];
            int size = checker.GetTypeSize(type), alignment = checker.GetTypeAlignment(type);
            string label = type is NamedTypeExpression named && checker.GetClassByIdentity(named.Name) != null ? "Object size" : "Size";
            return [new(label, $"{size} bytes"), new("Alignment", $"{alignment} bytes")];
        }
        catch (TypeCheckException) { return []; }

        bool Known(TypeExpression candidate) => candidate switch
        {
            PointerTypeExpression p => Known(p.Inner), ManagedTypeExpression p => Known(p.Inner),
            FunctionPointerTypeExpression { IsSignature: false } => true,
            ArrayTypeExpression a => a.Size != null && Known(a.ElementType),
            NamedTypeExpression n => n.TypeArguments.Count == 0 && (TypeChecker.IsPrimitive(n.Name) || checker.GetClassByIdentity(n.Name) != null || checker.GetStructByIdentity(n.Name) != null || checker.GetInterface(n.Name) != null || checker.ResolveEnum(n) != null),
            _ => false
        };
    }

    // Shorten only if the visible type name uniquely denotes this declaration.
    // Use the request location: imports at the declaration can differ from the caller's.
    private string ShortType(string text, string? path, int offset, Dictionary<int, Symbol>? shortened = null)
    {
        if (path == null) return text;
        var visible = Visible(path, offset).Where(s => IsType(s.Syntax.Node) || s.Syntax.Node is AliasDeclaration).ToArray();
        var typeParameters = ContextParents(path, offset).SelectMany(n => n.Node switch
        {
            MethodDeclaration m => m.GenericParameters.Select(p => p.Name),
            ClassDeclaration c => c.GenericParameters.Select(p => p.Name),
            StructDeclaration st => st.GenericParameters.Select(p => p.Name),
            InterfaceDeclaration i => i.GenericParameters.Select(p => p.Name),
            ExtensionDeclaration e => e.GenericParameters.Select(p => p.Name),
            _ => Enumerable.Empty<string>()
        }).ToHashSet();
        int delta = 0;
        return System.Text.RegularExpressions.Regex.Replace(text, @"\b[A-Za-z_][A-Za-z0-9_]*(?:::[A-Za-z_][A-Za-z0-9_]*)+", match =>
        {
            string simple = match.Value.Split("::").Last();
            // A bare primitive spelling denotes the built-in type, even when
            // an imported declaration has the same source name.
            if (TypeChecker.IsPrimitive(simple)) return match.Value;
            var matches = visible.Where(s => s.Name == simple).DistinctBy(s => s.Qualified).ToArray();
            if (typeParameters.Contains(simple) || matches.Length != 1 || matches[0].Qualified != match.Value) return match.Value;
            shortened?.Add(match.Index + delta, matches[0]);
            delta += simple.Length - match.Length;
            return simple;
        });
    }

    private object? CompoundTypeHover(string path, int offset, VisualStudioHover? presentation, bool showLayout)
    {
        var ts = tokens[path];
        int index = TokenAt(path, offset);
        if (index < 0 || ts[index].Kind is TokenKind.Identifier or TokenKind.DoubleColon || TypeChecker.IsPrimitive(ts[index].Text)) return null;
        var syntax = nodes[path].Where(n => n.Node is TypeExpression && Contains(n, offset))
            .OrderByDescending(n => n.Span.Length).FirstOrDefault();
        if (syntax?.Node is not TypeExpression type || type is NamedTypeExpression && !type.IsReadOnlyValue) return null;
        return TypeHover(type, syntax.Span, path, offset, presentation, showLayout);
    }

    private object? InferredTypeHover(string path, int offset, VisualStudioHover? presentation, bool showLayout)
    {
        int index = TokenAt(path, offset);
        if (index < 0 || tokens[path][index] is not { Kind: TokenKind.Identifier, Text: "var" } token ||
            snapshot.Checker is not { } checker) return null;
        // Capture retains the original var node even after checking replaces the
        // declaration's Type. Match that source token, not initializer syntax.
        var syntax = nodes[path].FirstOrDefault(node => node.Span.Start == token.Start &&
            node.Node is NamedTypeExpression { Name: "var", Namespace: null } &&
            node.Parent?.Node is VariableDeclaration { IsTypeInferred: true });
        if (syntax?.Parent?.Node is not VariableDeclaration declaration) return null;
        var type = checker.GetType(declaration);
        if (TypeChecker.TypeName(type) is "<error>" or "var") return null;
        return TypeHover(type, token.Span, path, offset, presentation, showLayout);
    }

    private object TypeHover(TypeExpression type, SourceSpan span, string path, int offset,
        VisualStudioHover? presentation, bool showLayout)
    {
        var parts = TypeDisplayParts(type, path, offset);
        string text = string.Concat(parts.Select(p => p.Text));
        var layout = showLayout ? TypeLayoutDetails(type) : [];
        return new HoverResult(new("markdown", "```gflat\n" + text + "\n```" + (layout.Length == 0 ? "" : "\n\n" + string.Join("  \n", layout.Select(d => d.Markdown)))),
            Workspace.ToRange(span), presentation?.Render(parts.ToArray(), layout, "type.public"));
    }

    private DisplayPart[] TypeDisplayParts(TypeExpression type, string path, int offset)
    {
        string text = ShortType(DisplayType(type), path, offset);
        var parts = new List<DisplayPart>();
        var lexed = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
        int cursor = 0;
        var context = symbols.Where(s => s.NameSpan.Source?.Path == path && s.Scope != null && Contains(s.Scope, offset))
            .MinBy(s => s.Scope!.Span.Length);
        for (int i = 0; i < lexed.Length; i++)
        {
            var token = lexed[i];
            if (token.Start > cursor) parts.Add(new("text", text[cursor..token.Start]));
            int first = i;
            while (first >= 2 && lexed[first - 1].Kind == TokenKind.DoubleColon) first -= 2;
            var target = token.Kind == TokenKind.Identifier ? TypeSymbol(string.Concat(lexed[first..(i + 1)].Select(t => t.Text)), context) : null;
            if (target == null && token.Kind == TokenKind.Identifier)
                target = Visible(path, offset).FirstOrDefault(s => (s.IsTypeParameter || IsType(s.Syntax.Node)) && s.Name == token.Text);
            parts.Add(new(target?.Classification ?? (TypeChecker.IsPrimitive(token.Text) || token.Kind is TokenKind.Readonly or TokenKind.Immutable or TokenKind.Managed ? "keyword" : token.Kind == TokenKind.Identifier ? "type" : char.IsDigit(token.Text.FirstOrDefault()) ? "number" : "punctuation"), token.Text, Target: SymbolLocation(target)));
            cursor = token.End + 1;
        }
        return parts.ToArray();
    }

    private static string Glyph(Symbol symbol)
    {
        if (symbol.IsTypeParameter) return "type.public";
        string kind = symbol.Syntax.Node switch
        {
            FieldDeclaration { IsConst: true } or VariableDeclaration { IsConst: true } => "constant",
            ConstructorDeclaration or MethodDeclaration => "method",
            FieldDeclaration => "field", AliasDeclaration => "type", _ => symbol.Classification
        };
        string access = symbol.Access switch { TokenKind.Private => "private", TokenKind.Protected => "protected", TokenKind.Internal => "internal", _ => "public" };
        return kind + "." + access;
    }
}
