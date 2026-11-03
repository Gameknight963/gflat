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
        var symbol = DocumentationReferenceAt(path, offset)?.Symbol ?? (index < 0 ? null : Resolve(path, index));
        if (symbol?.Syntax.Node is ConstructorDeclaration)
            symbol = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == symbol.Owner);
        return symbol?.Syntax.Node is VariableDeclaration or Parameter or FieldDeclaration or PropertyDeclaration or
                MethodDeclaration or ClassDeclaration or StructDeclaration or InterfaceDeclaration or EnumDeclaration or
                EnumMemberDeclaration or AliasDeclaration or NamespaceDeclaration &&
            symbol.NameSpan.Length > 0 && symbol.NameSpan.Source is { } source && !source.Path.StartsWith('<')
            ? symbol : null;
    }

    public PrepareRenameResult? PrepareRename(string path, int offset)
    {
        if (MacroAt(path, offset) is { Classification: "macro", Declaration.Source: { } macroSource } macro && !macroSource.Path.StartsWith('<'))
        {
            if (snapshot.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
                throw new InvalidOperationException("Rename requires an error-free project.");
            return new(Workspace.ToRange(macro.Span), macro.Name);
        }
        var symbol = RenameSymbol(path, offset);
        if (symbol == null) return null;
        var span = DocumentationReferenceAt(path, offset)?.Span ?? tokens[path][TokenAt(path, offset)].Span;
        return new(Workspace.ToRange(span), symbol.Name);
    }

    public RenameEdit[] Rename(string path, int offset, string newName)
    {
        var macro = MacroAt(path, offset) is { Classification: "macro", Declaration.Source: { } macroSource } selectedMacro &&
            !macroSource.Path.StartsWith('<') ? selectedMacro : null;
        var symbol = RenameSymbol(path, offset);
        if (symbol == null && macro == null) throw new InvalidOperationException("Select a source declaration or reference to rename.");
        var bag = new DiagnosticBag();
        var nameTokens = Lexer.Tokenize(new SourceFile("<rename>", newName), bag);
        if (bag.HasErrors || nameTokens.Count != 2 || nameTokens[0].Kind != TokenKind.Identifier ||
            nameTokens[0].Start != 0 || nameTokens[0].Span.Length != newName.Length)
            throw new InvalidOperationException("The new name must be one identifier and cannot be a keyword.");
        EnsureReferences();
        var family = symbol == null ? [] : RenameFamily(symbol).DistinctBy(s => s.NameSpan).ToArray();
        var names = family.Select(s => s.NameSpan).ToHashSet();
        if (symbol != null && IsType(symbol.Syntax.Node))
            foreach (var destructor in activeNodes.Where(n => n.Node is DestructorDeclaration &&
                Owner(Parents(n)) == symbol.Qualified && n.Span.Source != null))
            {
                var name = tokens[destructor.Span.Source!.Path].FirstOrDefault(t => t.Start >= destructor.Span.Start &&
                    t.End < destructor.Span.Start + destructor.Span.Length && t.Kind == TokenKind.Identifier && t.Text == symbol.Name);
                if (name != null) names.Add(name.Span);
            }
        var spans = macro != null
            ? snapshot.MacroSymbols.Where(s => s.Classification == "macro" && s.Declaration == macro.Declaration).Select(s => s.Span)
                .Append(macro.Declaration).Distinct().ToArray()
            : references.Where(pair => pair.Key.Kind == TokenKind.Identifier && pair.Value != null && names.Contains(pair.Value.NameSpan)).Select(pair => pair.Key.Span)
                .Concat(documentationReferences.Where(pair => names.Contains(pair.Value.NameSpan)).Select(pair => pair.Key))
                .Concat(names).Distinct().OrderBy(span => span.Start).ToArray();
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
        var changedFiles = changedSources.ToDictionary(file => Path.GetFullPath(file.Path), Workspace.Paths);
        var changedOptions = snapshot.Options with { DependencyObserver = null,
            IncludeLoader = path => changedFiles.GetValueOrDefault(path) ?? snapshot.Options.IncludeLoader?.Invoke(path) };
        var roots = snapshot.RootSources.Select(source => changedFiles.GetValueOrDefault(Path.GetFullPath(source.Path)) ?? source).ToArray();
        var changedSnapshot = new AnalysisSnapshot(roots, changedOptions);
        if (changedSnapshot.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error) is { } renameError)
            throw new InvalidOperationException("Rename would introduce compiler errors or conflicting declarations: " + renameError.Message);
        var changedModel = new EditorModel(changedSnapshot, changedSources);
        foreach (var original in documentationReferences)
        {
            if (original.Key.Source is not { } document || document.Path.StartsWith('<')) continue;
            var actual = changedModel.DocumentationReferenceAt(document.Path, Map(document.Path, original.Key.Start));
            if (actual == null || !Workspace.Paths.Equals(actual.Value.Symbol.NameSpan.Source?.Path, original.Value.NameSpan.Source?.Path) ||
                actual.Value.Symbol.NameSpan.Start != Map(original.Value.NameSpan.Source!.Path, original.Value.NameSpan.Start))
                throw new InvalidOperationException("Rename would change a documentation reference.");
        }
        foreach (var original in snapshot.MacroSymbols.Where(m => m.Classification == "macro" && m.Declaration.Source is { } source && !source.Path.StartsWith('<')))
        {
            var actual = changedSnapshot.MacroSymbols.FirstOrDefault(m => m.Classification == "macro" &&
                Workspace.Paths.Equals(m.Span.Source?.Path, original.Span.Source?.Path) &&
                m.Span.Start == Map(original.Span.Source!.Path, original.Span.Start));
            if (actual == null || !Workspace.Paths.Equals(actual.Declaration.Source?.Path, original.Declaration.Source?.Path) ||
                actual.Declaration.Start != Map(original.Declaration.Source!.Path, original.Declaration.Start))
                throw new InvalidOperationException("Rename would change which macro a name refers to.");
        }
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

    private IEnumerable<Symbol> RenameFamily(Symbol symbol)
    {
        yield return symbol;
        if (symbol.IsTypeParameter) yield break;
        if (symbol.Syntax.Node is NamespaceDeclaration)
        {
            foreach (var other in symbols.Where(s => s.Syntax.Node is NamespaceDeclaration && s.Qualified == symbol.Qualified)) yield return other;
        }
        if (IsType(symbol.Syntax.Node))
        {
            foreach (var constructor in symbols.Where(s => s.Owner == symbol.Qualified &&
                s.Syntax.Node is ConstructorDeclaration or MethodDeclaration { StaticConstructorName: not null })) yield return constructor;
        }
        if (symbol.Syntax.Node is MethodDeclaration or PropertyDeclaration && symbol.Owner != null)
        {
            var owner = symbols.FirstOrDefault(s => IsType(s.Syntax.Node) && s.Qualified == symbol.Owner);
            if (owner == null) yield break;
            var roots = AncestorTypes(owner).Append(owner).Where(type => symbols.Any(s => s.Owner == type.Qualified &&
                (s.Syntax.Node is PropertyDeclaration || ContractMember(s)) && SameMember(symbol, s))).ToArray();
            foreach (var type in symbols.Where(s => !s.IsTypeParameter && IsType(s.Syntax.Node)))
                if (roots.Any(root => root.NameSpan == type.NameSpan || AncestorTypes(type).Any(parent => parent.NameSpan == root.NameSpan)))
                    foreach (var member in symbols.Where(s => s.Owner == type.Qualified && SameMember(symbol, s))) yield return member;
        }
    }
}
