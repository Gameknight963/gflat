using Standard::Collections;
using Standard::Memory;
namespace Standard::IO;

/// Owns a fixed read-ahead buffer and borrows one source cursor without closing it.
/// All consuming access to that cursor must pass through this adapter while it is used.
/// Move transfers unread bytes; the noncopyable Vector field prevents cursor copies.
public struct BufferedReader : IByteReader
{
    private IByteReader* source;
    private Vector<char> buffer;
    private nuint start;
    private nuint end;
    private bool failed;

    /// Allocates and initializes one buffer; zero capacity rejects before buffer allocation.
    /// Keep the concrete source alive, unmoved and usable throughout adapter use.
    public BufferedReader(IByteReader* input, nuint capacity = 4096)
    {
        source = input;
        buffer = new Vector<char>();
        start = 0;
        end = 0;
        failed = false;
        if (capacity == 0) throw new IOException("Buffered reader capacity must not be zero");
        buffer.Reserve(capacity);
        for (nuint index = 0; index < capacity; index++) buffer.Add('\0');
    }

    public nuint BufferedCount { immutable get nothrow => end - start; }
    public nuint Capacity { immutable get nothrow => buffer.Count; }

    /// Returns a buffered prefix or performs one source refill, allowing short results.
    /// Zero count validates the source without consuming buffered bytes or dereferencing data.
    /// Source errors or over-reporting latch failure; later reads reject without source calls.
    /// Caller output must be writable and disjoint from this adapter and its source state.
    public nuint Read(char* data, nuint count)
    {
        if (failed) throw new IOException("Buffered reader cannot continue after input failure");
        if (count == 0)
        {
            failed = true;
            if (source.Read(data, 0) != 0)
                throw new IOException("Byte reader returned more than requested");
            failed = false;
            return 0;
        }
        Span<char> storage = buffer.AsSpan();
        char* scratch = storage.Data!;
        if (start == end)
        {
            failed = true;
            nuint read = source.Read(scratch, buffer.Count);
            if (read > buffer.Count)
                throw new IOException("Byte reader returned more than requested");
            start = 0;
            end = read;
            failed = false;
            if (read == 0) return 0;
        }
        nuint take = end - start;
        if (take > count) take = count;
        for (nuint index = 0; index < take; index++) data[index] = scratch[start + index];
        start += take;
        return take;
    }
}
