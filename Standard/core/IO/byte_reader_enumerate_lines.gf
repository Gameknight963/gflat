namespace Standard::IO;

/// Creates a deferred line sequence. No input is read until MoveNext.
/// LF and CRLF are removed. Bare CR, embedded zeros and high bytes remain counted.
/// Empty terminated lines and a final nonempty unterminated line are yielded.
/// Reuses one owned String per iterator. Copy a yielded line to retain it.
/// EOF ends that iterator. Errors propagate and invalidate Current. A later
/// MoveNext retries the source at its then-current position, without rollback.
/// No logical read-ahead: wrap the source once in BufferedReader when useful,
/// then keep reading through that same buffered reader to retain its unread tail.
public LineSequence EnumerateLines(IByteReader* input) => new LineSequence(input);

extension IByteReader
{
    public LineSequence EnumerateLines()
        => Standard::IO::EnumerateLines(this);
}
