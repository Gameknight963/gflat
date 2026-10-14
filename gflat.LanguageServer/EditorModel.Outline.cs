using gflat.ast;
using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private sealed record OutlineSymbol(string Name, string Detail, int Kind, TextRange Range,
        TextRange SelectionRange, OutlineSymbol[] Children);

    public object DocumentSymbols(string path, bool hierarchical = true)
    {
        var entries = nodes.GetValueOrDefault(path, []).Select(syntax => (Syntax: syntax, Kind: OutlineKind(syntax.Node)))
            .Where(entry => entry.Kind != 0).OrderBy(entry => entry.Syntax.Span.Start).ToArray();
        var included = entries.Select(entry => entry.Syntax).ToHashSet(ReferenceEqualityComparer.Instance);
        var parents = entries.ToDictionary(entry => entry.Syntax,
            entry => Parents(entry.Syntax).FirstOrDefault(included.Contains));
        var children = entries.Where(entry => parents[entry.Syntax] != null)
            .GroupBy(entry => parents[entry.Syntax]!).ToDictionary(group => group.Key, group => group.ToArray());

        (string Name, SourceSpan Selection, string Detail) DescribeOutline(Syntax syntax)
        {
            var node = syntax.Node;
            string ownerName = Parents(syntax).Where(parent => IsType(parent.Node)).Select(parent => SourceName(parent.Node)).FirstOrDefault() ?? "";
            string name = node switch {
                NamespaceDeclaration ns => ns.Name,
                ConstructorDeclaration => ownerName,
                DestructorDeclaration => "~" + ownerName,
                OperatorDeclaration op => "operator " + op.OperatorSymbol,
                _ => Describe(node).Name ?? ""
            };
            var symbol = declarations.GetValueOrDefault(node);
            string selectedName = node switch {
                NamespaceDeclaration => name.Split("::")[0],
                DestructorDeclaration => name[1..],
                OperatorDeclaration => "operator",
                _ => name
            };
            var selection = node is ConstructorDeclaration or DestructorDeclaration ? NameSpan(syntax, selectedName, null)
                : symbol?.NameSpan ?? NameSpan(syntax, selectedName, Describe(node).Type);
            string detail = symbol == null ? "" : Detail(symbol with { Name = name, NameSpan = selection });
            return (name, selection, detail);
        }

        if (!hierarchical) return entries.Select(entry => {
            var description = DescribeOutline(entry.Syntax);
            string? container = parents[entry.Syntax] is { } parent ? DescribeOutline(parent).Name : null;
            return (object)new { name = description.Name, kind = entry.Kind,
                location = new Location(Workspace.FileUri(path), Workspace.ToRange(description.Selection)), containerName = container };
        }).ToArray();

        OutlineSymbol Build(Syntax syntax, int kind)
        {
            var description = DescribeOutline(syntax);
            return new(description.Name, description.Detail, kind, Workspace.ToRange(syntax.Span),
                Workspace.ToRange(description.Selection), children.GetValueOrDefault(syntax, [])
                    .Select(entry => Build(entry.Syntax, entry.Kind)).ToArray());
        }
        return entries.Where(entry => parents[entry.Syntax] == null).Select(entry => Build(entry.Syntax, entry.Kind)).ToArray();
    }

    private static int OutlineKind(AstNode node) => node switch {
        NamespaceDeclaration => 3, ClassDeclaration => 5, StructDeclaration => 23,
        InterfaceDeclaration => 11, EnumDeclaration => 10, EnumMemberDeclaration => 22,
        ConstructorDeclaration => 9, DestructorDeclaration => 6, OperatorDeclaration => 25,
        MethodDeclaration { PropertyName: null } => 6, ExternDeclaration => 12,
        PropertyDeclaration => 7, FieldDeclaration { IsConst: true } => 14,
        FieldDeclaration => 8, AliasDeclaration => 5, _ => 0
    };
}
