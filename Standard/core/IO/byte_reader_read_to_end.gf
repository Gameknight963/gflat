using Standard::Text;
namespace Standard::IO;

/// Clears text and fills it with the remaining counted bytes, reusing its capacity.
/// Throws IOException if input exceeds maximumBytes or reports an invalid count.
/// Earlier chunks remain after failure. Input may have consumed additional bytes.
/// Borrows input until return and leaves it open. Source state and storage must be independent
/// of text, and source callbacks must not access text through another alias.
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

/// Returns an owning String containing the remaining bytes, including embedded zeros.
/// The source stays open. A byte limit applies to input length, excluding String overhead.
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
