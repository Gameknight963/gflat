namespace Standard::IO;

public void ReadExactly(IByteReader* input, byte* data, nuint count)
    => Standard::IO::ReadExactly(input, data.reinterpret<char*>(), count);

public nuint ReadAtLeast(IByteReader* input, byte* data, nuint capacity,
    nuint minimumBytes, bool throwOnEndOfStream = true)
    => Standard::IO::ReadAtLeast(input, data.reinterpret<char*>(), capacity, minimumBytes, throwOnEndOfStream);

public nuint CopyTo(IByteReader* input, ITextWriter* output, byte* scratch, nuint capacity)
    => Standard::IO::CopyTo(input, output, scratch.reinterpret<char*>(), capacity);

extension IByteReader
{
    public void ReadExactly(byte* data, nuint count)
        => Standard::IO::ReadExactly(this, data, count);

    public nuint ReadAtLeast(byte* data, nuint capacity,
        nuint minimumBytes, bool throwOnEndOfStream = true)
        => Standard::IO::ReadAtLeast(this, data, capacity, minimumBytes, throwOnEndOfStream);

    public nuint CopyTo(ITextWriter* output, byte* scratch, nuint capacity)
        => Standard::IO::CopyTo(this, output, scratch, capacity);
}
