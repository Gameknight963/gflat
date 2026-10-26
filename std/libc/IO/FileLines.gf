namespace std::IO;

// Borrows a binary File; never closes it. LF and CRLF terminate lines.
public static class FileLines
{
    // Append one line to a writer. A bare CR and embedded zero bytes are retained.
    // False means EOF before any input; an empty terminated line returns true.
    public static bool TryReadLine(File* input, ITextWriter* output)
    {
        char[256] chunk;
        nuint count = 0;
        bool any = false;
        bool pendingCR = false;
        while (true)
        {
            int value = input.ReadByte();
            if (value == -1)
            {
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

    // Capacity includes the terminator. Overflow consumes the whole line,
    // stores its terminated prefix, sets length, then throws.
    public static bool TryReadLine(File* input, char* data, nuint capacity, ref nuint length)
    {
        length = 0;
        if (capacity == 0) throw new BufferTooSmallException();
        BufferWriter output = new BufferWriter(data, capacity);
        defer { length = output.Length; }
        bool read = FileLines::TryReadLine(input, &output);
        if (output.Overflow) throw new BufferTooSmallException();
        return read;
    }

    public static nuint ReadLine(File* input, char* data, nuint capacity)
    {
        nuint length = 0;
        if (!FileLines::TryReadLine(input, data, capacity, ref length))
            throw new EndOfStreamException();
        return length;
    }

    private struct BufferWriter : ITextWriter
    {
        private char* data;
        private nuint capacity;
        public nuint Length;
        public bool Overflow;
        public BufferWriter(char* storage, nuint size)
        {
            data = storage; capacity = size; Length = 0; Overflow = false;
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
}
