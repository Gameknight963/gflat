using Standard::Text;

namespace Standard::IO;

// Clears text and reads the remaining bytes into it, reusing capacity.
// Exceeding maximumBytes throws IOException. Failures retain earlier chunks and may consume more input.
// Source state and storage must be independent of text, including access through callback aliases.
public void ReadToEnd(IByteReader* input, ref String text, nuint maximumBytes = (nuint)0 - (nuint)1)
{
    text.Clear();
    char[4096] scratch;
    while (true)
    {
        nuint remaining = maximumBytes - text.Length;
        nuint request = 4096;
        if (remaining < 4096) request = remaining + 1;
        nuint received = input.Read(scratch, request);
        if (received > request)
            throw new IOException("Byte reader returned more than requested");
        if (received == 0) return;
        if (received > remaining)
            throw new IOException("Input exceeds maximum byte count");
        text.AppendLiteral(scratch, received);
    }
}

// Returns the remaining counted bytes as an owning String and leaves the source open.
// The limit counts input bytes, excluding String capacity and overhead.
public String ReadToEnd(IByteReader* input, nuint maximumBytes = (nuint)0 - (nuint)1)
{
    String text = new();
    Standard::IO::ReadToEnd(input, ref text, maximumBytes);
    return move text;
}

extension IByteReader
{
    public void ReadToEnd(ref String text, nuint maximumBytes = (nuint)0 - (nuint)1)
        => Standard::IO::ReadToEnd(this, ref text, maximumBytes);

    public String ReadToEnd(nuint maximumBytes = (nuint)0 - (nuint)1)
        => Standard::IO::ReadToEnd(this, maximumBytes);
}
