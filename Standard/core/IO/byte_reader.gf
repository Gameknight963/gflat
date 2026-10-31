namespace Standard::IO;

// Returns one byte from 0 through 255, or -1 at EOF.
// Makes one one-byte read and throws IOException for an invalid count.
public int ReadByte(IByteReader* input)
{
    char value = '\0';
    nuint received = input.Read(&value, 1);
    if (received > 1) throw new IOException("Byte reader returned more than requested");
    if (received == 0) return -1;
    return (int)(byte)value;
}

extension IByteReader
{
    public int ReadByte()
    => Standard::IO::ReadByte(this);
}

// Fills count bytes through short reads, throwing EndOfStreamException on early EOF.
// Failures can leave consumed input and a written prefix. Zero count still validates the source.
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
    => Standard::IO::ReadExactly(this, data, count);
}

// Copies remaining bytes to output through caller scratch and returns the byte count.
// Source, sink, and scratch must be independent. Failure can leave consumed input and partial output.
// Both endpoints stay open. Flush or complete the sink when required.
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
    => Standard::IO::CopyTo(this, output, scratch, capacity);
}

// Copies remaining input using 4096 bytes of stack scratch.
// Both endpoints stay open. Failure can leave consumed input and partial output.
public nuint CopyTo(IByteReader* input, ITextWriter* output)
{
    char[4096] scratch;
    return CopyTo(input, output, scratch, 4096);
}

extension IByteReader
{
    public nuint CopyTo(ITextWriter* output)
    => Standard::IO::CopyTo(this, output);
}
