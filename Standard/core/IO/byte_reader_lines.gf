namespace Standard::IO;

// Appends a line to output, removing LF or CRLF. Bare CR and embedded zeros remain data.
// Returns false at EOF before a line. Empty terminated and final nonempty lines return true.
// An error can leave input consumed and output partly written. Unwritten scratch bytes may be lost.
// Source and sink must be independent. Keep buffered input behind one adapter.
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
