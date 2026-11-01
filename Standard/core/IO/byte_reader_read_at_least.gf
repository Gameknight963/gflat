namespace Standard::IO;

// Reads until minimumBytes are available, allowing a read to fill the remaining capacity.
// Early EOF throws unless throwOnEndOfStream is false. Failure may leave a written prefix.
public nuint ReadAtLeast(IByteReader* input, char* data, nuint capacity,
    nuint minimumBytes, bool throwOnEndOfStream = true)
{
    if (minimumBytes > capacity)
        throw new IOException("Minimum byte count exceeds buffer capacity");
    if (minimumBytes == 0)
    {
        if (input.Read(data, 0) != 0)
            throw new IOException("Byte reader returned more than requested");
        return 0;
    }
    nuint total = 0;
    while (total < minimumBytes)
    {
        nuint remaining = capacity - total;
        nuint received = input.Read(data + total, remaining);
        if (received > remaining)
            throw new IOException("Byte reader returned more than requested");
        if (received == 0)
        {
            if (throwOnEndOfStream) throw new EndOfStreamException();
            return total;
        }
        total += received;
    }
    return total;
}

extension IByteReader
{
    public nuint ReadAtLeast(char* data, nuint capacity,
        nuint minimumBytes, bool throwOnEndOfStream = true)
        => Standard::IO::ReadAtLeast(this, data, capacity, minimumBytes, throwOnEndOfStream);
}
