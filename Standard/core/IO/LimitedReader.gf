namespace Standard::IO;

// Borrows a source cursor and permits at most limit bytes.
// Keep the source alive and unmoved. Copies share its cursor but track separate allowances.
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

    // Unused byte allowance. Early EOF can leave it positive.
    // An input error preserves this value and prevents further reads.
    public nuint Remaining { immutable get nothrow => remaining; }

    // Reads once, capped to the remaining allowance.
    // Zero count or exhausted allowance returns without accessing source or output.
    // Input errors and invalid counts prevent further reads. Output must be separate from adapter and source state.
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
