using gflat.ast;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private readonly HashSet<Token> typeArgumentSeparators = new(ReferenceEqualityComparer.Instance);

    private void IndexTypeArgumentSeparators()
    {
        var gaps = new Dictionary<string, List<(int Start, int End)>>(Workspace.Paths);
        // Do not ignore an entire type span: array bounds can contain real calls.
        // Only the gaps between captured type children contain type separators.
        foreach (var siblings in snapshot.Nodes.Where(syntax => syntax.Node is TypeExpression &&
                     syntax.Parent?.Node is TypeExpression or CallExpression)
                     .GroupBy(syntax => syntax.Parent!, ReferenceEqualityComparer.Instance))
        {
            var children = siblings.Where(syntax => syntax.Span.Source != null && syntax.Span.Length > 0)
                .OrderBy(syntax => syntax.Span.Start).ToArray();
            for (int i = 1; i < children.Length; i++)
            {
                var previous = children[i - 1].Span;
                var current = children[i].Span;
                int start = previous.Start + previous.Length;
                if (previous.SourceId != current.SourceId || start >= current.Start) continue;
                string path = current.Source!.Path;
                if (!gaps.TryGetValue(path, out var ranges)) gaps[path] = ranges = [];
                ranges.Add((start, current.Start));
            }
        }
        foreach (var (path, ranges) in gaps)
        {
            if (!tokens.TryGetValue(path, out var fileTokens)) continue;
            ranges.Sort((left, right) => left.Start.CompareTo(right.Start));
            int cursor = 0, end = -1;
            foreach (var token in fileTokens)
            {
                if (token.Kind != TokenKind.Comma) continue;
                while (cursor < ranges.Count && ranges[cursor].Start <= token.Start)
                    end = Math.Max(end, ranges[cursor++].End);
                if (token.Start < end) typeArgumentSeparators.Add(token);
            }
        }
    }
}
