using Standard::Text;

namespace Standard::IO;

// Clears line and reads into its reusable storage. EOF returns false with an empty String.
// An error can leave a partial line. Input must be independent of the String's backing storage.
public bool TryReadLine(IByteReader* input, ref String line)
{
    line.Clear();
    return Standard::IO::TryReadLine(input, &line);
}

// Returns an owning line, or throws EndOfStreamException if EOF precedes it.
// An empty terminated line returns an empty String.
public String ReadLine(IByteReader* input)
{
    String line = new();
    if (!Standard::IO::TryReadLine(input, ref line)) throw new EndOfStreamException();
    return move line;
}

extension IByteReader
{
    public bool TryReadLine(ref String line)
    => Standard::IO::TryReadLine(this, ref line);

    public String ReadLine()
    => Standard::IO::ReadLine(this);
}
