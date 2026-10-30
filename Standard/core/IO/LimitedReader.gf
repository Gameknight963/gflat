namespace Standard::IO;

/// Borrows one source cursor and permits at most the specified number of bytes.
/// No read-ahead, buffer allocation, closing or ownership transfer is performed.
/// Keep the source alive and unmoved; do not interleave other consuming access.
/// Copies share the source cursor but have independent remaining allowances.
public struct LimitedReader : IByteReader
{
    private IByteReader* source;
    private nuint remaining;
    private bool failed;

    public LimitedReader(IByteReader* input, nuint limit) nothrow
    {
        source = input;
        remaining = limit;
        failed = false;
    }

    /// Unused allowance, not the source length. Early EOF leaves this positive.
    /// After failure it remains the last validated allowance; reads then reject.
    public nuint Remaining { immutable get nothrow => remaining; }

    /// Performs at most one source read, capped to the unused allowance.
    /// Zero count or exhausted allowance never calls source or touches data.
    /// Source errors/over-reporting latch failure because consumption is unknown.
    /// Caller output must hold min(count, Remaining) writable bytes.
    /// Caller storage must be disjoint from this adapter and its source state.
    public nuint Read(char* data, nuint count)
    {
        if (failed) throw new IOException("Limited reader cannot continue after input failure");
        if (count == 0 || remaining == 0) return 0;
        nuint allowed = count;
        if (allowed > remaining) allowed = remaining;
        failed = true;
        nuint received = source.Read(data, allowed);
        if (received > allowed)
            throw new IOException("Byte reader returned more than requested");
        remaining -= received;
        failed = false;
        return received;
    }
}
