namespace Console;

// Writes bytes before the first zero through stdout's current text mode.
// A failure can leave partial output.
void Write(readonly(char)* message)
{
    nuint length = 0;
    while (message[length] != '\0') length++;
    Write(message, length);
}

// Writes counted bytes, including zeros, through stdout's current text mode.
// A failure can leave partial output.
void Write(readonly(char)* data, nuint length)
{
    if (length == 0) return;
    void* stream = __gflat_stdio_stdout();
    if (fwrite((readonly(void)*)data, 1, length, stream) != length || ferror(stream) != 0)
        throw new Standard::IO::IOException("Console write failed");
}

// Writes a platform newline through stdout's current text mode.
void WriteLine() { Write("\n", 1); }
// Writes bytes before the first zero, followed by a platform newline.
void WriteLine(readonly(char)* message) { Write(message); WriteLine(); }
// Writes counted bytes followed by a platform newline.
// A failure can leave partial output.
void WriteLine(readonly(char)* data, nuint length) { Write(data, length); WriteLine(); }

// Flushes stdout and throws IOException if the host stream reports a failure.
void Flush()
{
    if (fflush(__gflat_stdio_stdout()) != 0)
        throw new Standard::IO::IOException("Console flush failed");
}

// Reads up to count bytes through stdin's current mode. EOF can return fewer bytes.
// IO errors throw IOException after possible partial input.
nuint Read(char* data, nuint count)
{
    if (count == 0) return 0;
    void* stream = __gflat_stdio_stdin();
    nuint read = fread(data, 1, count, stream);
    if (read < count && ferror(stream) != 0)
        throw new Standard::IO::IOException("Console read failed");
    return read;
}

// Returns the next byte from zero through 255, or -1 at EOF. IO errors throw IOException.
int ReadByte()
{
    void* stream = __gflat_stdio_stdin();
    int value = fgetc(stream);
    if (value == -1 && ferror(stream) != 0)
        throw new Standard::IO::IOException("Console read failed");
    return value;
}

// Returns the next stdin byte without consuming it, or -1 at EOF.
// Reading or pushback failure throws IOException.
int PeekByte()
{
    void* stream = __gflat_stdio_stdin();
    int value = fgetc(stream);
    if (value == -1)
    {
        if (ferror(stream) != 0) throw new Standard::IO::IOException("Console read failed");
        return -1;
    }
    if (ungetc(value, stream) == -1) throw new Standard::IO::IOException("Console lookahead failed");
    return value;
}

// Clears stdin's sticky EOF and error indicators, preserving buffered input.
void ClearInputError() { clearerr(__gflat_stdio_stdin()); }

// Fills count bytes or throws EndOfStreamException on early EOF.
// A failure can leave consumed input and a written prefix.
void ReadExactly(char* data, nuint count)
{
    if (Read(data, count) != count) throw new Standard::IO::EndOfStreamException();
}

// Appends a line to output, removing LF or CRLF after host text translation.
// EOF before a line returns false. Empty terminated and final nonempty lines return true.
// A failure can stop midline with consumed input and partial output.
bool TryReadLine(ITextWriter* output) { return detail::ReadLine(output); }

// Reads a line into capacity bytes, including room for its trailing zero.
// EOF before a line returns false. LF and CRLF endings are removed after host translation.
// Zero capacity throws BufferTooSmallException before reading.
// Overflow consumes the line, then throws BufferTooSmallException with its terminated prefix and length retained.
bool TryReadLine(char* data, nuint capacity, ref nuint length)
{
    length = 0;
    if (capacity == 0) throw new Standard::Text::BufferTooSmallException();
    detail::BufferWriter writer = new detail::BufferWriter(data, capacity);
    defer { length = writer.Length; }
    bool read = detail::ReadLine(&writer);
    if (writer.Overflow) throw new Standard::Text::BufferTooSmallException();
    return read;
}

// Returns the line length, or throws EndOfStreamException if no line remains.
// Uses TryReadLine's terminated-buffer and overflow rules.
nuint ReadLine(char* data, nuint capacity)
{
    nuint length = 0;
    if (!TryReadLine(data, capacity, ref length)) throw new Standard::IO::EndOfStreamException();
    return length;
}

namespace detail
{
    struct BufferWriter : ITextWriter
    {
        private char* data;
        private nuint capacity;
        public nuint Length;
        public bool Overflow;
        public BufferWriter(char* storage, nuint size)
        {
            data = storage;
            capacity = size;
            Length = 0;
            Overflow = false;
            data[0] = '\0';
        }
        public void Write(readonly(char)* text, nuint count)
        {
            for (nuint i = 0; i < count; i++)
            {
                if (Length < capacity - 1) { data[Length] = text[i]; Length++; }
                else Overflow = true;
            }
            data[Length] = '\0';
        }
    }

    // Share byte reading between caller buffers and owning String conveniences.
    // CRLF and LF end a line; a bare CR remains part of the text.
    bool ReadLine(ITextWriter* output)
    {
        char[256] chunk;
        nuint count = 0;
        bool any = false;
        bool pendingCR = false;
        void* stream = __gflat_stdio_stdin();
        while (true)
        {
            int value = fgetc(stream);
            if (value == -1)
            {
                if (ferror(stream) != 0) throw new Standard::IO::IOException("Console read failed");
                if (pendingCR) { chunk[count] = '\r'; count++; }
                if (count != 0) output.Write(chunk, count);
                return any;
            }
            if (value == 10)
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
            pendingCR = value == 13;
            if (!pendingCR)
            {
                chunk[count] = (char)value; count++;
                if (count == 256) { output.Write(chunk, count); count = 0; }
            }
        }
    }
}

private extern void* __gflat_stdio_stdin();
private extern void* __gflat_stdio_stdout();
private extern nuint fwrite(readonly(void)* data, nuint size, nuint count, void* stream);
private extern nuint fread(char* data, nuint size, nuint count, void* stream);
private extern int fflush(void* stream);
private extern int fgetc(void* stream);
private extern int ungetc(int value, void* stream);
private extern int ferror(void* stream);
private extern void clearerr(void* stream);
