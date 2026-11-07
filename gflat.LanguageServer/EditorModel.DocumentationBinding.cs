using System.Text.Json;
using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    public IReadOnlyList<DocumentationIssue> DocumentationIssues => documentationIssues;

    private Symbol? DocumentationTarget(Symbol declaration, string reference)
    {
        string name = reference.TrimStart('@');
        int paren = name.IndexOf('(');
        string? signature = paren < 0 ? null : name[(paren + 1)..].TrimEnd(')');
        if (paren >= 0) name = name[..paren];
        int generic = name.IndexOf('<');
        if (generic >= 0) name = name[..generic];
        string bare = name.Split(["::", "."], StringSplitOptions.None).Last();
        var parameters = symbols.Where(s => s.Scope == declaration.Syntax && s.Name == name);
        var visible = name == bare ? Visible(declaration.NameSpan.Source!.Path, declaration.NameSpan.Start, bare) : symbols.Where(s =>
            s.Scope == null && (s.Qualified == name.Replace(".", "::", StringComparison.Ordinal) || s.Qualified == Join(declaration.Namespace, name.Replace(".", "::", StringComparison.Ordinal))));
        var candidates = parameters.Concat(visible)
            .Where(s => s.Name == bare && Accessible(s, declaration.NameSpan.Source!.Path, declaration.NameSpan.Start))
            .DistinctBy(s => s.NameSpan).ToArray();
        if (signature != null)
        {
            string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
            candidates = candidates.Where(s => s.Parameters != null &&
                Normalize(string.Join(",", s.Parameters.Select(p => DisplayType(p.Type)))) == Normalize(signature)).ToArray();
        }
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private void ValidateDocumentation(Symbol symbol, DocumentationComment documentation)
    {
        documentationIssues.AddRange(documentation.Issues);
        var seen = new HashSet<(string Kind, string? Name)>();
        foreach (var section in documentation.Sections)
        {
            if (section.Kind is "param" or "tparam" or "return" && !seen.Add((section.Kind, section.Name)))
                documentationIssues.Add(new(section.NameSpan.Source != null ? section.NameSpan : symbol.NameSpan, "Duplicate @" + section.Kind + " documentation"));
            if (section.Kind == "param")
            {
                var parameter = symbols.FirstOrDefault(s => s.Syntax.Node is Parameter && s.Scope == symbol.Syntax && s.Name == section.Name);
                if (parameter == null) documentationIssues.Add(new(section.NameSpan, "No parameter named '" + section.Name + "'"));
                else documentationReferences[section.NameSpan] = parameter;
            }
            if (section.Kind == "tparam")
            {
                var parameter = symbols.FirstOrDefault(s => s.IsTypeParameter && s.Scope == symbol.Syntax && s.Name == section.Name);
                if (parameter == null) documentationIssues.Add(new(section.NameSpan, "No type parameter named '" + section.Name + "'"));
                else documentationReferences[section.NameSpan] = parameter;
            }
            if (section.Kind == "throws" && section.Name != null)
            {
                var exception = DocumentationTarget(symbol, section.Name);
                if (exception == null || !IsType(exception.Syntax.Node)) documentationIssues.Add(new(section.NameSpan, "Unresolved exception type '" + section.Name + "'"));
                else IndexDocumentationReference(section.NameSpan, section.Name, exception);
            }
        }
        foreach (var part in documentation.Summary.Concat(documentation.Sections.SelectMany(s => s.Content)).Where(p => p.Kind == "reference"))
        {
            var target = DocumentationTarget(symbol, part.Text);
            if (target == null) documentationIssues.Add(new(part.Span, "Unresolved or ambiguous documentation reference '" + part.Text + "'"));
            else IndexDocumentationReference(part.Span, part.Text, target);
        }
    }

    private void IndexDocumentationReference(SourceSpan span, string spelling, Symbol target)
    {
        if (span.Source == null || span.Length != spelling.Length) return;
        int suffix = spelling.IndexOfAny(['(', '<']);
        string prefix = suffix < 0 ? spelling : spelling[..suffix];
        int name = prefix.LastIndexOf(target.Name, StringComparison.Ordinal);
        if (name >= 0) documentationReferences[span.Source.Span(span.Start + name, target.Name.Length)] = target;
    }

    private (SourceSpan Span, Symbol Symbol)? DocumentationReferenceAt(string path, int offset)
    {
        foreach (var pair in documentationReferences)
            if (Workspace.Paths.Equals(pair.Key.Source?.Path, path) && offset >= pair.Key.Start && offset < pair.Key.Start + pair.Key.Length)
                return (pair.Key, pair.Value);
        return null;
    }

    private DisplayPart[] DocumentationParts(Symbol symbol, IEnumerable<DocumentationInline> content) => content.Select(part =>
    {
        var target = part.Kind == "reference" ? DocumentationTarget(DocumentedDeclaration(symbol) ?? symbol, part.Text) : null;
        string kind = target?.Classification ?? (part.Kind == "code" && Lexer.Tokenize(new SourceFile("<documentation>", part.Text), new diagnostics.DiagnosticBag()) is { Count: 2 } lexed &&
            lexed[0].Kind != TokenKind.Identifier && char.IsLetter(part.Text.FirstOrDefault()) ? "keyword" : part.Kind == "code" ? "identifier" : "text");
        return new DisplayPart(kind, part.Text, Target: target == null ? null : NavigationLocation(target),
            ClassificationStyle: part.Kind is "code" or "reference" ? VisualStudioHover.UseClassificationFont : 0);
    }).ToArray();

    private string? ParameterDocumentation(Symbol symbol, string name)
    {
        var owner = DocumentedDeclaration(symbol);
        if (owner == null) return null;
        int index = symbol.Parameters?.ToList().FindIndex(p => p.Name == name) ?? -1;
        string documentedName = index >= 0 && index < (owner.Parameters?.Count ?? 0) ? owner.Parameters![index].Name : name;
        return StructuredDocumentation(symbol)?.Sections.FirstOrDefault(s => s.Kind == "param" && s.Name == documentedName) is { } section
            ? DocumentationComment.Text(section.Content) : null;
    }
}
