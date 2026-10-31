namespace Console;

/// Writes bytes through the current stdout text mode up to the first zero.
/// Adds no newline and does not explicitly flush. Failures can leave partial output.
void Write(immutable(char*) message)
{
    nuint length = 0;
    while (message[length] != '\0') length++;
    Write(message, length);
}

/// Writes the counted bytes, including embedded zeros, through the current stdout mode.
/// Does not explicitly flush or retry. Failures can leave partial output.
void Write(immutable(char*) data, nuint length)
{
    if (length == 0) return;
    void* stream = __gflat_stdio_stdout();
    if (fwrite((readonly(void)*)data, 1, length, stream) != length || ferror(stream) != 0)
        throw new Standard::IO::IOException("Console write failed");
}

/// Writes a newline through the current stdout mode without explicitly calling Flush.
void WriteLine() { Write("\n", 1); }
/// Writes bytes before the first zero, then a newline, without explicitly calling Flush.
void WriteLine(immutable(char*) message) { Write(message); WriteLine(); }
/// Writes counted bytes, including zeros, then a newline through the current stdout mode.
/// A text or newline write failure can leave partial output.
void WriteLine(immutable(char*) data, nuint length) { Write(data, length); WriteLine(); }

/// Flushes stdout and throws IOException if the host stream reports a failure.
void Flush()
{
    if (fflush(__gflat_stdio_stdout()) != 0)
        throw new Standard::IO::IOException("Console flush failed");
}

/// Reads up to count bytes from the current stdin mode and returns the byte count.
/// Adds no terminator or line processing. Zero count consumes nothing.
/// IO errors throw IOException after possible partial input. EOF may return fewer bytes.
nuint Read(char* data, nuint count)
{
    if (count == 0) return 0;
    void* stream = __gflat_stdio_stdin();
    nuint read = fread(data, 1, count, stream);
    if (read < count && ferror(stream) != 0)
        throw new Standard::IO::IOException("Console read failed");
    return read;
}

/// Returns the next byte from zero through 255, or -1 at EOF. IO errors throw IOException.
int ReadByte()
{
    void* stream = __gflat_stdio_stdin();
    int value = fgetc(stream);
    if (value == -1 && ferror(stream) != 0)
        throw new Standard::IO::IOException("Console read failed");
    return value;
}

/// Returns the next stdin byte without consuming it on success, or -1 at EOF.
/// Read or pushback failure throws IOException. Uses the same stream mode and position as other reads.
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

/// Clears sticky stdin EOF and error indicators without discarding buffered input.
/// Does not repair the underlying descriptor, change stream mode or rewind.
void ClearInputError() { clearerr(__gflat_stdio_stdin()); }

/// Requires count bytes or throws EndOfStreamException when EOF arrives early.
/// The available prefix is already consumed and stored. Actual IO errors throw IOException.
void ReadExactly(char* data, nuint count)
{
    if (Read(data, count) != count) throw new Standard::IO::EndOfStreamException();
}

/// Appends a line without its ending or an added terminator to the borrowed sink.
/// Does not clear, flush, close or destroy the sink. LF and CRLF endings are removed after host translation.
/// False means EOF before a line. Blank lines and nonempty final unterminated lines return true.
/// Sink exceptions propagate unchanged. Consumed input and prior output are not rolled back.
/// A sink or IO failure can stop midline. Remaining input is not automatically drained.
bool TryReadLine(ITextWriter* output) { return detail::ReadLine(output); }

/// Reads a line into caller storage. Capacity includes room for a trailing zero.
/// False means EOF before a line. Blank lines return true with zero length.
/// Removes LF and CRLF endings after host input translation. Final unterminated lines are returned.
/// Zero capacity throws BufferTooSmallException before accessing stdin or the buffer.
/// On a completed overflow read, drains the line then throws BufferTooSmallException. Its terminated prefix and length remain.
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

/// Returns the line byte count or throws EndOfStreamException when no line remains.
/// Uses the same capacity and overflow rules as TryReadLine and leaves a trailing zero.
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
