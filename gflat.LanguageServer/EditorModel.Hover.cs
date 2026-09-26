using gflat.ast;
using gflat.CompileExceptions;
using gflat.diagnostics;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private string DisplayType(TypeExpression type) => snapshot.Checker?.DisplayTypeName(type) ?? TypeChecker.TypeName(type).Replace(".", "::");
    private string Detail(Symbol symbol) => string.Concat(Signature(symbol).Select(p => p.Text));

    private DisplayPart[] Signature(Symbol symbol)
    {
        var parts = new List<DisplayPart>();
        void Add(string text, string kind = "text") { if (text.Length > 0) parts.Add(new(kind, text)); }
        void Keyword(string word) { Add(word, "keyword"); Add(" "); }
        void Type(TypeExpression type)
        {
            string text = DisplayType(type);
            var tokens = Lexer.Tokenize(text).Where(t => t.Kind != TokenKind.EndOfFile).ToArray();
            int cursor = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                var token = tokens[i];
                Add(text[cursor..token.Start]);
                string kind = TypeChecker.IsPrimitive(token.Text) ? "keyword" : token.Kind == TokenKind.Identifier
                    ? i + 1 < tokens.Length && tokens[i + 1].Kind == TokenKind.DoubleColon ? "namespace" : "type"
                    : char.IsLetter(token.Text.FirstOrDefault()) ? "keyword" : char.IsDigit(token.Text.FirstOrDefault()) ? "number" : "punctuation";
                Add(text[token.Start..(token.End + 1)], kind);
                cursor = token.End + 1;
            }
            Add(text[cursor..]);
        }
        void Name()
        {
            var names = symbol.Qualified.Split("::");
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) Add("::", "punctuation");
                Add(names[i], i == names.Length - 1 ? symbol.Classification : symbol.Owner != null && i == names.Length - 2 ? "type" : "namespace");
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
                Type(parameter.Type); Add(" "); Add(parameter.Name, "parameter");
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
        if (snapshot.Checker is not { } checker || snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) ||
            symbol.Parameters != null || symbol.Syntax.Node is NamespaceDeclaration or PropertyDeclaration ||
            symbol.Syntax.Node is ClassDeclaration { IsGeneric: true } or StructDeclaration { IsGeneric: true }) return [];
        TypeExpression? type = IsType(symbol.Syntax.Node) ? new NamedTypeExpression(symbol.Qualified, null, 0) : symbol.Type;
        if (type == null) return [];
        var recorded = checker.GetType(symbol.Syntax.Node);
        if (TypeChecker.TypeName(recorded) != "<error>") type = recorded;
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
