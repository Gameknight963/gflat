using Standard::Collections;
using Standard::Memory;
namespace Standard::IO;

/// Buffers reads in owned or borrowed scratch and borrows one source cursor without closing it.
/// All consuming access to that cursor must pass through this adapter while it is used.
/// Move transfers the cursor and storage reference; the Vector owner field prevents copies.
public struct BufferedReader : IByteReader
{
    private IByteReader* source;
    private Vector<char> buffer;
    private char*? scratch;
    private nuint capacity;
    private nuint start;
    private nuint end;
    private bool failed;

    /// Allocates and initializes one buffer; zero capacity rejects before buffer allocation.
    /// Keep the concrete source alive, unmoved and usable throughout adapter use.
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

    /// Borrows caller scratch without allocating or initializing its bytes.
    /// Zero capacity rejects before accessing storage; errors use normal exception storage.
    /// Keep scratch alive, unmoved and disjoint from reader/source state and their retained input.
    /// No scratch bytes are freed on destruction; this cursor remains noncopyable.
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

    /// Returns a buffered prefix or performs one source refill, allowing short results.
    /// Zero count validates the source without consuming buffered bytes or dereferencing data.
    /// Source errors or over-reporting latch failure; later reads and peeks reject without source calls.
    /// Caller output must be writable and disjoint from this adapter, scratch and source state.
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

    /// Returns the next byte from 0 through 255, or -1 when a refill reports EOF.
    /// Does not consume buffered input. Empty buffers may read ahead through the source.
    /// Keep using this same adapter to consume the peeked byte and retained suffix.
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
