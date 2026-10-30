namespace Console;

// Borrows process stdout; this value owns no stream and does not close it.
// The counted write preserves embedded null bytes. Flush explicitly when needed.
struct Writer : ITextWriter
{
    public void Write(readonly(char)* data, nuint length)
    {
        Console::Write(data, length);
    }
}
