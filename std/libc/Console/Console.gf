namespace Console;

// C strings stop at the first null. The counted overload writes every byte.
void Write(readonly(char*) message)
{
    nuint length = 0;
    while (message[length] != '\0') length++;
    Write(message, length);
}

void Write(readonly(char*) data, nuint length)
{
    if (length == 0) return;
    if (fwrite((readonly(void)*)data, 1, length, __gflat_stdio_stdout()) != length)
        throw new std::IOException("Console write failed");
}

void WriteLine() { Write("\n", 1); }
void WriteLine(readonly(char*) message) { Write(message); WriteLine(); }
void WriteLine(readonly(char*) data, nuint length) { Write(data, length); WriteLine(); }

void Flush()
{
    if (fflush(__gflat_stdio_stdout()) != 0)
        throw new std::IOException("Console flush failed");
}

// capacity includes the terminator. No String or application buffer allocation is needed.
// On overflow the whole line is consumed and a terminated prefix remains.
bool TryReadLine(char* data, nuint capacity, ref nuint length)
{
    length = 0;
    if (capacity == 0) throw new Exception("Console buffer capacity must include a terminator");
    detail::BufferWriter writer = new detail::BufferWriter(data, capacity);
    defer { length = writer.Length; }
    bool read = detail::ReadLine(&writer);
    if (writer.Overflow) throw new std::BufferTooSmallException();
    return read;
}

nuint ReadLine(char* data, nuint capacity)
{
    nuint length = 0;
    if (!TryReadLine(data, capacity, ref length)) throw new std::EndOfStreamException();
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
                if (ferror(stream) != 0) throw new std::IOException("Console read failed");
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
private extern int fflush(void* stream);
private extern int fgetc(void* stream);
private extern int ferror(void* stream);
