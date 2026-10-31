namespace Standard::IO;

// Creates a deferred sequence that consumes one byte per move.
// An error invalidates Current. The next move retries at the source's current position.
public ByteSequence EnumerateBytes(IByteReader* input) => new ByteSequence(input);

extension IByteReader
{
    public ByteSequence EnumerateBytes()
        => Standard::IO::EnumerateBytes(this);
}
