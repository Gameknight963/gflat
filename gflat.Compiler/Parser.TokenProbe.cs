namespace gflat;

public partial class Parser
{
    private int speculativeDepth;
    private int tokenProbeDepth;
    private List<TokenSplit>? tokenSplits;

    private readonly record struct TokenSplit(List<Token> Tokens, int Position, Token Original);

    private sealed class SpeculativeParseException : Exception;

    // Keep the caller's token list untouched, then journal splits in the
    // parser-owned list so nested probes can undo only their own changes.
    private sealed class TokenProbe : IDisposable
    {
        private readonly Parser parser;
        private readonly List<Token> tokens;
        private readonly int position;
        private readonly bool copyOnSplit;
        private readonly int splitMark;
        private bool completed;
        private bool speculative;

        public TokenProbe(Parser parser, bool failFast = true)
        {
            this.parser = parser;
            tokens = parser._tokens;
            position = parser._pos;
            copyOnSplit = parser._copyTokensOnSplit;
            splitMark = parser.tokenSplits?.Count ?? 0;
            parser.tokenProbeDepth++;
            speculative = failFast;
            if (speculative) parser.speculativeDepth++;
        }

        public void EndSpeculation()
        {
            if (!speculative) return;
            parser.speculativeDepth--;
            speculative = false;
        }

        public void Commit()
        {
            if (!completed) Complete();
        }

        public void Rollback()
        {
            if (completed) return;
            if (parser.tokenSplits is { } splits)
            {
                for (int i = splits.Count - 1; i >= splitMark; i--)
                {
                    var split = splits[i];
                    split.Tokens.RemoveAt(split.Position + 1);
                    split.Tokens[split.Position] = split.Original;
                }
                splits.RemoveRange(splitMark, splits.Count - splitMark);
            }
            parser._tokens = tokens;
            parser._pos = position;
            parser._copyTokensOnSplit = copyOnSplit;
            Complete();
        }

        private void Complete()
        {
            EndSpeculation();
            completed = true;
            if (--parser.tokenProbeDepth == 0) parser.tokenSplits?.Clear();
        }

        public void Dispose()
        {
            if (!completed) Rollback();
        }
    }
}
