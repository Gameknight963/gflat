namespace gflat;

public partial class Parser
{
    // Speculative parsing shares the list until Expect needs to split a >>.
    // Nested probes restore their own position and copy-on-write ownership.
    private sealed class TokenProbe : IDisposable
    {
        private readonly Parser parser;
        private readonly List<Token> tokens;
        private readonly int position;
        private readonly bool copyOnSplit;
        private bool completed;

        public TokenProbe(Parser parser)
        {
            this.parser = parser;
            tokens = parser._tokens;
            position = parser._pos;
            copyOnSplit = parser._copyTokensOnSplit;
            parser._copyTokensOnSplit = true;
        }

        public void Commit()
        {
            parser._copyTokensOnSplit = ReferenceEquals(parser._tokens, tokens) ? copyOnSplit : false;
            completed = true;
        }

        public void Rollback()
        {
            parser._tokens = tokens;
            parser._pos = position;
            parser._copyTokensOnSplit = copyOnSplit;
            completed = true;
        }

        public void Dispose()
        {
            if (!completed) Rollback();
        }
    }
}
