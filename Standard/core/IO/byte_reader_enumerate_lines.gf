namespace Standard::IO;

// Creates deferred counted lines, removing LF and CRLF endings.
// Each iterator reuses one String. Copy a line to retain it across moves.
// An error invalidates Current. The next move retries at the source's current position.
// Use one BufferedReader for read-ahead and continue through it to retain unread input.
public LineSequence EnumerateLines(IByteReader* input) => new LineSequence(input);

extension IByteReader
{
    public LineSequence EnumerateLines()
        => Standard::IO::EnumerateLines(this);
}
