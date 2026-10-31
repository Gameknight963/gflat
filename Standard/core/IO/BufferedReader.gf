using Standard::Collections;
using Standard::Memory;

namespace Standard::IO;

// Buffers reads in owned or borrowed scratch while borrowing one source cursor.
// Keep the source alive and unmoved. Consume through this adapter while it holds unread bytes.
public struct BufferedReader : IByteReader
{
    private IByteReader* source;
    private Vector<char> buffer;
    private char*? scratch;
    private nuint capacity;
    private nuint start;
    private nuint end;
    private bool failed;

    // Allocates a buffer. Zero capacity throws before allocation.
    // Keep the source alive and unmoved throughout adapter use.
    public BufferedReader(IByteReader* input, nuint capacity = 4096)
    {
        source = input;
        buffer = new Vector<char>();
        scratch = null;
        this.capacity = capacity;
        start = 0;
        end = 0;
        failed = false;
        if (capacity == 0) throw new IOException("Buffered reader capacity must not be zero");
        buffer.Reserve(capacity);
        for (nuint index = 0; index < capacity; index++) buffer.Add('\0');
        Span<char> storage = buffer.AsSpan();
        scratch = storage.Data!;
    }

    // Borrows caller scratch until the adapter is destroyed. Zero capacity throws.
    // Keep scratch alive, unmoved, and separate from reader state and retained source input.
    public BufferedReader(IByteReader* input, char* storage, nuint capacity)
    {
        source = input;
        buffer = new Vector<char>();
        scratch = null;
        this.capacity = capacity;
        start = 0;
        end = 0;
        failed = false;
        if (capacity == 0) throw new IOException("Buffered reader capacity must not be zero");
        scratch = storage;
    }

    public nuint BufferedCount { immutable get nothrow => end - start; }
    public nuint Capacity { immutable get nothrow => capacity; }

    // Reads a buffered prefix or refills once, allowing short results.
    // Zero count validates the source while preserving buffered input.
    // A source error prevents further reads or peeks. Output must be separate from adapter and source storage.
    public nuint Read(char* data, nuint count)
    {
        if (count == 0)
        {
            if (failed) throw new IOException("Buffered reader cannot continue after input failure");
            failed = true;
            if (source.Read(data, 0) != 0)
                throw new IOException("Byte reader returned more than requested");
            failed = false;
            return 0;
        }
        if (!EnsureBuffered()) return 0;
        nuint take = end - start;
        if (take > count) take = count;
        for (nuint index = 0; index < take; index++) data[index] = scratch![start + index];
        start += take;
        return take;
    }

    // Returns the next byte, or -1 at EOF, keeping it buffered.
    // An empty buffer can read ahead. Continue through this adapter to retain that input.
    public int PeekByte()
    {
        if (!EnsureBuffered()) return -1;
        return (int)(byte)scratch![start];
    }

    private bool EnsureBuffered()
    {
        if (failed) throw new IOException("Buffered reader cannot continue after input failure");
        if (start != end) return true;
        failed = true;
        nuint read = source.Read(scratch!, capacity);
        if (read > capacity)
            throw new IOException("Byte reader returned more than requested");
        start = 0;
        end = read;
        failed = false;
        return read != 0;
    }
}
