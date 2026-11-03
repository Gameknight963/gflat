using System.Security.Cryptography;
using System.Text;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly Dictionary<SourceSpan, string> documentationFiles = [];
    private DocumentationEntry[]? exportedDocumentation;

    private string DocumentationId(Symbol symbol) => symbol.Syntax.Node.GetType().Name + ":" + symbol.Qualified +
        (symbol.Parameters == null ? "" : "(" + string.Join(",", symbol.Parameters.Select(p => DisplayType(p.Type))) + ")");

    public DocumentationEntry[] ExportDocumentation()
    {
        if (exportedDocumentation != null) return exportedDocumentation;
        var paths = snapshot.RootSources.Select(s => s.Path).Concat(snapshot.PreprocessorDependencies).ToHashSet(Workspace.Paths);
        return exportedDocumentation = symbols.Where(s => !s.IsTypeParameter && s.Scope == null && s.NameSpan.Source != null && paths.Contains(s.NameSpan.Source.Path) &&
            StructuredDocumentation(s) != null && NavigationLocation(s) != null).DistinctBy(s => s.NameSpan).Select(symbol =>
        {
            var documentation = StructuredDocumentation(symbol)!;
            DocumentationText[] Parts(IEnumerable<DocumentationInline> parts) => parts.Select(part => new DocumentationText(part.Kind, part.Text,
                part.Kind == "reference" && DocumentationTarget(DocumentedDeclaration(symbol) ?? symbol, part.Text) is { } target ? DocumentationId(target) : null)).ToArray();
            return new DocumentationEntry(DocumentationId(symbol), symbol.Qualified, Detail(symbol), NavigationLocation(symbol)!, Parts(documentation.Summary),
                documentation.Sections.Select(section => new DocumentationEntrySection(section.Kind, section.Name, Parts(section.Content))).ToArray());
        }).OrderBy(e => e.Id, StringComparer.Ordinal).ToArray();
    }

    private string FullDocumentation(Symbol symbol)
    {
        var documentation = StructuredDocumentation(symbol);
        var text = new StringBuilder(Detail(symbol)).AppendLine().AppendLine();
        if (documentation == null) return text.ToString();
        text.AppendLine(documentation.SummaryText);
        foreach (var section in documentation.Sections)
        {
            text.AppendLine();
            if (section.Kind is not ("details" or "code"))
                text.Append(section.Kind switch { "param" => "Parameter", "tparam" => "Type parameter", "return" => "Returns", "throws" => "Throws", _ => section.Kind })
                    .Append(section.Name == null ? "" : " " + section.Name).AppendLine(":");
            text.AppendLine(DocumentationComment.Text(section.Content));
        }
        return text.ToString();
    }

    private Location DocumentationLocation(Symbol symbol)
    {
        if (!documentationFiles.TryGetValue(symbol.NameSpan, out string? file))
        {
            string text = FullDocumentation(symbol);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
            string directory = Path.Combine(Path.GetTempPath(), "gflat", "documentation");
            Directory.CreateDirectory(directory);
            file = Path.Combine(directory, hash + ".txt");
            if (!File.Exists(file)) File.WriteAllText(file, text);
            documentationFiles[symbol.NameSpan] = file;
        }
        return new(Workspace.FileUri(file), new(new(0, 0), new(0, 0)));
    }
}
