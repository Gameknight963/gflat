namespace Standard::IO;

/// Sequentially discards exactly count bytes through bounded reads, without seeking.
/// Uses 4096 bytes of stack scratch; no buffer is allocated on the heap.
/// Short reads continue. Early EOF throws EndOfStreamException; source errors propagate.
/// Failures leave any consumed prefix discarded, without rollback or extra draining.
/// Zero count still validates the source through Read(data, 0), like ReadExactly.
/// Borrows input until return; keep its cursor alive, unmoved and exclusively consumed here.
/// Requests never exceed the remaining count; the source may have its own read-ahead policy.
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
