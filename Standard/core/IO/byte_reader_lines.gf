namespace Standard::IO;

/// Appends one counted byte line to a borrowed writer; never clears or closes it.
/// LF and CRLF end a line; bare CR, embedded zeros and high bytes remain data.
/// False means EOF before any line; empty terminated and final nonempty lines return true.
/// Uses stack scratch, no read-ahead or heap buffer. Wrap input once in BufferedReader when useful.
/// Source/writer errors propagate unchanged; consumed input and prior output are not rolled back.
/// An uncommitted scratch prefix can be lost on error; no remaining input is drained.
/// Keep source/sink alive, unmoved, and disjoint from each other and retained input/output storage.
public bool TryReadLine(IByteReader* input, ITextWriter* output)
{
    char[256] chunk;
    nuint count = 0;
    bool any = false;
    bool pendingCR = false;
    while (true)
    {
        char value = '\0';
        nuint received = input.Read(&value, 1);
        if (received > 1) throw new IOException("Byte reader returned more than requested");
        if (received == 0)
        {
            if (pendingCR) { chunk[count] = '\r'; count++; }
            if (count != 0) output.Write(chunk, count);
            return any;
        }
        if (value == '\n')
        {
            if (count != 0) output.Write(chunk, count);
            return true;
        }
        any = true;
        if (pendingCR)
        {
            chunk[count] = '\r'; count++;
            if (count == 256) { output.Write(chunk, count); count = 0; }
        }
        pendingCR = value == '\r';
        if (!pendingCR)
        {
            chunk[count] = value; count++;
            if (count == 256) { output.Write(chunk, count); count = 0; }
        }
    }
}

extension IByteReader
{
    public bool TryReadLine(ITextWriter* output)
    => Standard::IO::TryReadLine(this, output);
}
