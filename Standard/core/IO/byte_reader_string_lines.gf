using Standard::Text;
namespace Standard::IO;

/// Clears line first and reuses its capacity, matching Console's ref-String contract.
/// False leaves line empty. Errors may leave an already-appended prefix. No rollback or drain.
/// Input must not borrow line's storage, which Clear or later growth can change.
public bool TryReadLine(IByteReader* input, ref String line)
{
    line.Clear();
    return Standard::IO::TryReadLine(input, &line);
}

/// Returns an owning counted line, or throws EndOfStreamException before any line.
/// Empty terminated lines return an empty String. Errors use normal local cleanup.
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
