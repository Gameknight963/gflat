using Standard::IO;
namespace Standard::Text;

/// Borrows a writer and accepts at most limit bytes through this adapter.
/// Keep the sink alive,
/// usable and unmoved. Copies share the sink but have independent allowances.
public struct LimitedWriter : ITextWriter
{
    private ITextWriter* output;
    private nuint remaining;
    private bool failed;

    public LimitedWriter(ITextWriter* destination, nuint limit) nothrow
    {
        output = destination;
        remaining = limit;
        failed = false;
    }

    /// Unused allowance after fully successful writes, not physical output remaining.
    /// Sink failure preserves this value but makes all subsequent writes reject.
    public nuint Remaining { immutable get nothrow => remaining; }

    /// Oversized calls reject before touching input or sink and may be retried.
    /// Zero count is a no-op after failure-state validation, even when exhausted.
    /// Input must meet the sink's stability/overlap rules and not overlap this state.
    public void Write(readonly(char)* data, nuint count)
    {
        if (failed) throw new IOException("Limited writer cannot continue after output failure");
        if (count > remaining) throw new IOException("Write exceeds limited writer allowance");
        if (count == 0) return;
        failed = true;
        output.Write(data, count);
        remaining -= count;
        failed = false;
    }
}
