using System.Globalization;
using gflat.ast;
using gflat.comptime;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private object? QueryHover(string path, int offset, VisualStudioHover? presentation, bool showLayout)
    {
        int index = TokenAt(path, offset);
        if (index < 0 || tokens[path][index].Kind is not (TokenKind.Typeof or TokenKind.Nameof or TokenKind.Alignof or TokenKind.Sizeof)) return null;
        var token = tokens[path][index];
        var query = nodes[path].Where(n => n.Span.Start == token.Start &&
                n.Node is TypeofExpression or NameofExpression or AlignofExpression or SizeofExpression)
            .MinBy(n => n.Span.Length);
        if (query == null) return null;
        var parts = new List<DisplayPart>();
        var details = new List<HoverDetail>();
        bool known = snapshot.Checker?.TryGetConstValue(query.Node, out _) == true;
        ConstValue? value = null;
        if (known) snapshot.Checker!.TryGetConstValue(query.Node, out value);
        if (value is ConstValue.String text)
        {
            parts.Add(new("punctuation", "("));
            parts.AddRange(TypeDisplayParts(snapshot.Checker!.GetType(query.Node), path, offset));
            parts.Add(new("punctuation", ")"));
            parts.Add(new("string", System.Text.Json.JsonSerializer.Serialize(text.Value)));
        }
        else if (value is ConstValue.Integer number)
        {
            parts.AddRange([new("punctuation", "("), new("keyword", "int"), new("punctuation", ")"),
                new("number", number.Value.ToString(CultureInfo.InvariantCulture))]);
        }
        else
        {
            foreach (var sourceToken in tokens[path].Select((t, i) => (Token: t, Index: i))
                .Where(t => t.Token.Start >= query.Span.Start && t.Token.End < query.Span.Start + query.Span.Length))
            {
                var symbol = sourceToken.Token.Kind == TokenKind.Identifier ? Resolve(path, sourceToken.Index) : null;
                string kind = symbol?.Classification ?? (char.IsLetter(sourceToken.Token.Text.FirstOrDefault()) ? "keyword" : "punctuation");
                parts.Add(new(kind, sourceToken.Token.Text, Target: SymbolLocation(symbol)));
            }
            if (value is ConstValue.Struct descriptor && descriptor.Fields.TryGetValue("Name", out var name) && name is ConstValue.String typeName)
            {
                details.Add(new("Name", typeName.Value));
                if (descriptor.Fields.GetValueOrDefault("Kind") is ConstValue.Integer kind &&
                    snapshot.Checker!.GetEnum("Standard$TypeKind")?.Members.FirstOrDefault(p => p.Value == kind.Value).Key is { } kindName)
                    details.Add(new("Kind", kindName));
                if (showLayout && descriptor.Fields.GetValueOrDefault("HasLayout") is ConstValue.Boolean { Value: true })
                    foreach (string label in new[] { "Size", "Alignment" })
                        if (descriptor.Fields.GetValueOrDefault(label) is ConstValue.UInteger bytes)
                            details.Add(new(label, bytes.Value.ToString(CultureInfo.InvariantCulture) + " bytes"));
            }
            else details.Add(new("", "Depends on the generic type arguments or is unavailable until this expression is checked."));
        }
        string preview = string.Concat(parts.Select(p => p.Text));
        string markdown = "```gflat\n" + preview + "\n```" + (details.Count == 0 ? "" : "\n\n" + string.Join("  \n", details.Select(d => d.Markdown)));
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(query.Span),
            presentation?.Render(parts.ToArray(), details.ToArray(), "constant.public"));
    }
}
