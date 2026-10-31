using Standard::IO;

namespace Standard::Text;

// Borrows a writer and limits accepted bytes. Keep the sink alive and unmoved.
// Copies share the sink but track separate allowances.
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

    // Unused allowance after successful writes.
    // A sink failure preserves this count and prevents further writes.
    public nuint Remaining { immutable get nothrow => remaining; }

    // Writes bytes within the remaining allowance. Oversized calls reject before touching the sink.
    // Zero count only checks failure state. Input must be separate from this adapter and follow sink alias rules.
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
