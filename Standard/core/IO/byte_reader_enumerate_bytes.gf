namespace Standard::IO;

/// Creates a deferred byte sequence; no input is read until MoveNext.
/// Each yielded byte consumes one source byte, without read-ahead or allocation.
/// EOF ends that iterator. Errors propagate and invalidate Current; a later
/// MoveNext retries the source at its then-current position, without rollback.
public ByteSequence EnumerateBytes(IByteReader* input) => new ByteSequence(input);

extension IByteReader
{
    public ByteSequence EnumerateBytes()
        => Standard::IO::EnumerateBytes(this);
}
