using gflat.ast;
using gflat.diagnostics;

namespace gflat.LanguageServer;

public sealed record RenameEdit(string Path, TextRange Range, string NewText);
public sealed record PrepareRenameResult(TextRange Range, string Placeholder);

public sealed partial class EditorModel
{
    private Symbol? RenameSymbol(string path, int offset)
    {
        if (snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("Rename requires an error-free project; fix diagnostics first.");
        int index = TokenAt(path, offset);
        var symbol = index < 0 ? null : Resolve(path, index);
        return symbol?.Syntax.Node is VariableDeclaration or Parameter && symbol.Scope != null &&
            symbol.NameSpan.Length > 0 && symbol.NameSpan.Source is { } source && !source.Path.StartsWith('<')
            ? symbol : null;
    }

    public PrepareRenameResult? PrepareRename(string path, int offset)
    {
        var symbol = RenameSymbol(path, offset);
        if (symbol == null) return null;
        var token = tokens[path][TokenAt(path, offset)];
        return new(Workspace.ToRange(token.Span), symbol.Name);
    }

    public RenameEdit[] Rename(string path, int offset, string newName)
    {
        var symbol = RenameSymbol(path, offset) ?? throw new InvalidOperationException("Only local variables and explicit parameters can be renamed.");
        var bag = new DiagnosticBag();
        var nameTokens = Lexer.Tokenize(new SourceFile("<rename>", newName), bag);
        if (bag.HasErrors || nameTokens.Count != 2 || nameTokens[0].Kind != TokenKind.Identifier ||
            nameTokens[0].Start != 0 || nameTokens[0].Span.Length != newName.Length)
            throw new InvalidOperationException("The new name must be one identifier and cannot be a keyword.");
        var spans = references.Where(pair => pair.Value?.NameSpan == symbol.NameSpan).Select(pair => pair.Key.Span)
            .Append(symbol.NameSpan).Distinct().OrderBy(span => span.Start).ToArray();
        var offsets = spans.GroupBy(span => span.Source!.Path, Workspace.Paths).ToDictionary(group => group.Key, group => {
            var ordered = group.OrderBy(span => span.Start).ToArray();
            int[] prefix = new int[ordered.Length + 1];
            for (int i = 0; i < ordered.Length; i++) prefix[i + 1] = prefix[i] + newName.Length - ordered[i].Length;
            return (Starts: ordered.Select(span => span.Start).ToArray(), Prefix: prefix);
        }, Workspace.Paths);
        int Map(string sourcePath, int originalOffset)
        {
            if (!offsets.TryGetValue(sourcePath, out var map)) return originalOffset;
            int index = Array.BinarySearch(map.Starts, originalOffset);
            return originalOffset + map.Prefix[index < 0 ? ~index : index];
        }
        var changedSources = sources.Values.Where(source => !source.Path.StartsWith('<')).Select(source => {
            string text = source.Text;
            foreach (var span in spans.Where(span => Workspace.Paths.Equals(span.Source!.Path, source.Path)).OrderByDescending(span => span.Start))
                text = text.Remove(span.Start, span.Length).Insert(span.Start, newName);
            return new SourceFile(source.Path, text);
        }).ToArray();
        var changedSnapshot = new AnalysisSnapshot(changedSources, snapshot.Options);
        if (changedSnapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("Rename would introduce compiler errors or conflicting declarations.");
        var changedModel = new EditorModel(changedSnapshot, changedSources);
        foreach (var (token, original) in references)
        {
            if (token.Span.Source is not { } source || source.Path.StartsWith('<')) continue;
            int mapped = Map(source.Path, token.Start);
            int index = changedModel.TokenAt(source.Path, mapped);
            var actual = index < 0 ? null : changedModel.Resolve(source.Path, index);
            if (original == null && actual == null) continue;
            if (original == null || actual == null || original.Classification != actual.Classification ||
                !Workspace.Paths.Equals(original.NameSpan.Source?.Path, actual.NameSpan.Source?.Path) ||
                Map(original.NameSpan.Source!.Path, original.NameSpan.Start) != actual.NameSpan.Start)
                throw new InvalidOperationException("Rename would change which declaration an identifier refers to.");
        }
        return spans.Select(span => new RenameEdit(span.Source!.Path, Workspace.ToRange(span), newName)).ToArray();
    }
}
