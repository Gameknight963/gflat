using System.Globalization;
using gflat.ast;
using gflat.comptime;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private object? QueryHover(string path, int offset, VisualStudioHover? presentation, bool showLayout)
    {
        int index = TokenAt(path, offset);
        if (index < 0 || tokens[path][index].Kind is not (TokenKind.Typeof or TokenKind.Reflect or TokenKind.Nameof or TokenKind.Alignof or TokenKind.Sizeof)) return null;
        var token = tokens[path][index];
        var queries = nodes[path].Where(n => n.Span.Start == token.Start &&
                n.Node is TypeofExpression or ReflectExpression or NameofExpression or AlignofExpression or SizeofExpression)
            .ToArray();
        var query = queries.MinBy(n => n.Span.Length);
        if (query == null) return null;
        var parts = new List<DisplayPart>();
        var details = new List<HoverDetail>();
        bool known = snapshot.Checker?.TryGetConstValue(query.Node, out _) == true;
        ConstValue? value = null;
        if (known) snapshot.Checker!.TryGetConstValue(query.Node, out value);
        if (queries.Any(candidate => snapshot.Checker?.TryGetConstValue(candidate.Node, out var other) != true || !Equals(value, other)))
            value = null;
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
            if (value == null && query.Node is TypeofExpression or ReflectExpression)
                parts.AddRange([new("punctuation", "("), new("keyword", "reflection"), new("punctuation", ")")]);
            foreach (var sourceToken in tokens[path].Select((t, i) => (Token: t, Index: i))
                .Where(t => t.Token.Start >= query.Span.Start && t.Token.End < query.Span.Start + query.Span.Length))
            {
                var symbol = sourceToken.Token.Kind == TokenKind.Identifier ? Resolve(path, sourceToken.Index) : null;
                string kind = symbol?.Classification ?? (char.IsLetter(sourceToken.Token.Text.FirstOrDefault()) ? "keyword" : "punctuation");
                parts.Add(new(kind, sourceToken.Token.Text, Target: SymbolLocation(symbol)));
            }
            if (value is ConstValue.Reflection reflection)
            {
                if (reflection.Name.Length != 0) details.Add(new("Name", reflection.Name));
                details.Add(new("Kind", reflection.Kind == "Type" && reflection.Type != null ? ReflectedTypeKind(reflection.Type) : reflection.Kind));
                if (reflection is { Kind: "Value", Value: { } captured })
                    details.Add(new("Value", ReflectionValuePreview(captured)));
                if (reflection.Kind == "OverloadGroup" && reflection.Overloads != null)
                    details.Add(new("Overloads", reflection.Overloads.Count.ToString(CultureInfo.InvariantCulture)));
                if (reflection.Kind == "Type" && reflection.Type is { } reflectedType)
                {
                    if (showLayout) details.AddRange(TypeLayoutDetails(reflectedType));
                }
                else if (ReflectedSymbol(reflection) is { } reflectedSymbol)
                {
                    var signature = Signature(reflectedSymbol, navigable: true, hoverPath: path, hoverOffset: offset);
                    details.Add(new("Declaration", string.Concat(signature.Select(p => p.Text)), signature));
                }
                else if (reflection.Type is { } valueType)
                    details.Add(new("Type", DisplayType(valueType), TypeDisplayParts(valueType, path, offset)));
            }
            else if (queries.Length == 1 && query.Node is TypeofExpression typeQuery && ExpressionType(typeQuery.Operand, path, offset) is { } operandType)
                details.Add(new("Type", DisplayType(operandType), TypeDisplayParts(operandType, path, offset)));
            else if (snapshot.Diagnostics.FirstOrDefault(d => d.Severity == diagnostics.DiagnosticSeverity.Error &&
                d.Span.SourceId == query.Span.SourceId && d.Span.Start >= query.Span.Start &&
                d.Span.Start < query.Span.Start + query.Span.Length) is { } error)
                details.Add(new("Error", error.Message));
        }
        string preview = string.Concat(parts.Select(p => p.Text));
        string markdown = "```gflat\n" + preview + "\n```" + (details.Count == 0 ? "" : "\n\n" + string.Join("  \n", details.Select(d => d.Markdown)));
        return new HoverResult(new("markdown", markdown), Workspace.ToRange(query.Span),
            presentation?.Render(parts.ToArray(), details.ToArray(), "constant.public"));
    }

    private string ReflectionValuePreview(ConstValue value, int depth = 0)
    {
        if (depth == 8) return "…";
        return value switch {
            ConstValue.Reflection { Kind: "Type", Type: { } type } => "reflect(" + DisplayType(type) + ")",
            ConstValue.Reflection { Kind: "Value", Value: { } inner } => "reflect(" + ReflectionValuePreview(inner, depth + 1) + ")",
            ConstValue.Reflection declaration => "reflect(" + declaration.Name + ")",
            ConstValue.String text => System.Text.Json.JsonSerializer.Serialize(text.Value),
            ConstValue.Boolean flag => flag.Value ? "true" : "false",
            ConstValue.Integer number => number.Value.ToString(CultureInfo.InvariantCulture),
            ConstValue.UInteger number => number.Value.ToString(CultureInfo.InvariantCulture),
            ConstValue.Array array => $"{array.Elements.Count} items",
            _ => value.ToString() ?? ""
        };
    }
}
