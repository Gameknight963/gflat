using Standard::Collections;

namespace Standard::IO;

// Returns up to count bytes, stopping at EOF. Source remains open.
public Vector<byte> ReadBytes(IByteReader* input, nuint count)
{
    Vector<byte> result = new();
    char[4096] scratch;
    if (count == 0)
    {
        if (input.Read(scratch, 0) != 0)
            throw new IOException("Byte reader returned more than requested");
        return move result;
    }
    while (result.Count < count)
    {
        nuint request = count - result.Count;
        if (request > 4096) request = 4096;
        nuint received = input.Read(scratch, request);
        if (received > request)
            throw new IOException("Byte reader returned more than requested");
        if (received == 0) break;
        for (nuint i = 0; i < received; i++) result.Add((byte)scratch[i]);
    }
    return move result;
}

extension IByteReader
{
    public Vector<byte> ReadBytes(nuint count)
        => Standard::IO::ReadBytes(this, count);
}
