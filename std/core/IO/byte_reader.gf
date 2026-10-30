namespace std::IO;

/// Reads through short results until count bytes are stored, or throws on early EOF.
/// Earlier input and buffer writes remain on failure. Zero count still validates the source.
/// Borrows input and writable data for this call; they must remain alive and disjoint.
public void ReadExactly(IByteReader* input, char* data, nuint count)
{
    if (count == 0)
    {
        if (input.Read(data, 0) != 0)
            throw new IOException("Byte reader returned more than requested");
        return;
    }
    nuint offset = 0;
    while (offset < count)
    {
        nuint remaining = count - offset;
        nuint read = input.Read(data + offset, remaining);
        if (read > remaining)
            throw new IOException("Byte reader returned more than requested");
        if (read == 0) throw new EndOfStreamException();
        offset += read;
    }
}

extension IByteReader
{
    public void ReadExactly(char* data, nuint count)
    => std::IO::ReadExactly(this, data, count);
}

/// Pumps remaining bytes through caller scratch into a borrowed synchronous writer.
/// Source, sink and scratch must not overlap, and must remain alive and unmoved during the call.
/// Short reads continue; positive-request zero ends input. Returns the number sent to output.
/// Does not retain storage, allocate a buffer, close, flush or finalize either endpoint.
/// Failure may consume input or leave prior output; no rollback or draining is performed.
public nuint CopyTo(IByteReader* input, ITextWriter* output, char* scratch, nuint capacity)
{
    if (capacity == 0) throw new IOException("Copy buffer must not be empty");
    nuint total = 0;
    nuint maximum = (nuint)0 - (nuint)1;
    while (true)
    {
        nuint count = input.Read(scratch, capacity);
        if (count > capacity)
            throw new IOException("Byte reader returned more than requested");
        if (count == 0) return total;
        if (count > maximum - total)
            throw new IOException("Byte copy length overflow");
        output.Write(scratch, count);
        total += count;
    }
}

extension IByteReader
{
    public nuint CopyTo(ITextWriter* output, char* scratch, nuint capacity)
    => std::IO::CopyTo(this, output, scratch, capacity);
}

/// Copies using 4096 bytes of stack scratch, borrowing both endpoints until return.
/// Has the same EOF, disjointness, lifetime and partial-failure rules as the scratch overload.
public nuint CopyTo(IByteReader* input, ITextWriter* output)
{
    char[4096] scratch;
    return CopyTo(input, output, scratch, 4096);
}

extension IByteReader
{
    public nuint CopyTo(ITextWriter* output)
    => std::IO::CopyTo(this, output);
}
