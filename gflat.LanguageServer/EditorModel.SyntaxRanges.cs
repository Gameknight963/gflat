using Syntax = gflat.AnalysisSnapshot.Syntax;

namespace gflat.LanguageServer;

public sealed partial class EditorModel
{
    private static int TokenStartIndex(Token[] fileTokens, int offset)
    {
        int lower = 0, upper = fileTokens.Length;
        while (lower < upper)
        {
            int middle = lower + (upper - lower) / 2;
            if (fileTokens[middle].Start < offset) lower = middle + 1;
            else upper = middle;
        }
        return lower;
    }

    // Syntax spans overlap through their ancestors. Subtree end bounds let a
    // caret lookup skip preceding declarations while preserving capture-order ties.
    private sealed class SyntaxRangeIndex
    {
        private readonly record struct Entry(Syntax Syntax, int End, int Order);
        private readonly Entry[] entries;
        private readonly int[] maximumEnds;

        public SyntaxRangeIndex(Syntax[] syntax, IReadOnlyDictionary<Syntax, int> scopeEnds)
        {
            entries = syntax.Select((node, order) => new Entry(node,
                    scopeEnds.GetValueOrDefault(node, node.Span.Start + node.Span.Length), order))
                .OrderBy(entry => entry.Syntax.Span.Start).ThenBy(entry => entry.Order).ToArray();
            maximumEnds = new int[entries.Length];
            Build(0, entries.Length);
        }

        private int Build(int lower, int upper)
        {
            if (lower == upper) return int.MinValue;
            int middle = lower + (upper - lower) / 2;
            return maximumEnds[middle] = Math.Max(entries[middle].End,
                Math.Max(Build(lower, middle), Build(middle + 1, upper)));
        }

        public Syntax? Find(int offset)
        {
            int best = -1, length = int.MaxValue;
            Search(0, entries.Length);
            return best < 0 ? null : entries[best].Syntax;

            void Search(int lower, int upper)
            {
                if (lower == upper) return;
                int middle = lower + (upper - lower) / 2;
                if (maximumEnds[middle] <= offset) return;
                var entry = entries[middle];
                Search(lower, middle);
                if (entry.Syntax.Span.Start > offset) return;
                int width = entry.End - entry.Syntax.Span.Start;
                if (offset < entry.End && (width < length || width == length &&
                    (best < 0 || entry.Order < entries[best].Order)))
                {
                    best = middle;
                    length = width;
                }
                Search(middle + 1, upper);
            }
        }
    }
}
