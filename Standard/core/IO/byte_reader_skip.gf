namespace Standard::IO;

// Discards count bytes through sequential reads, throwing EndOfStreamException on early EOF.
// Failure leaves already consumed bytes discarded. Zero count still validates the source.
// Requests stay within the remaining count, though a buffered source can read ahead.
public void SkipExactly(IByteReader* input, nuint count)
{
    char[4096] scratch;
    if (count == 0)
    {
        ReadExactly(input, scratch, 0);
        return;
    }
    while (count != 0)
    {
        nuint chunk = count;
        if (chunk > 4096) chunk = 4096;
        ReadExactly(input, scratch, chunk);
        count -= chunk;
    }
}

extension IByteReader
{
    public void SkipExactly(nuint count)
    => Standard::IO::SkipExactly(this, count);
}
