namespace Standard::Text;

/// Buffers counted writes in borrowed scratch and drains full blocks to a borrowed sink.
/// Flush sends pending bytes only. Destruction performs no output or sink cleanup.
public struct BufferedWriter : ITextWriter
{
    private ITextWriter* output;
    private char* scratch;
    private nuint capacity;
    private nuint used;
    private bool failed;

    public BufferedWriter(ITextWriter* destination, char* storage, nuint size) nothrow
    {
        output = destination; scratch = storage; capacity = size;
        used = 0; failed = false;
    }

    /// Copies nonempty input, draining each full block. Zero capacity forwards directly.
    /// Input must be stable and disjoint from scratch and storage the sink may mutate.
    public void Write(readonly(char)* data, nuint count)
    {
        if (failed) throw new Exception("Buffered writer cannot continue after output failure");
        if (count == 0) return;
        if (capacity == 0)
        {
            failed = true;
            output.Write(data, count);
            failed = false;
            return;
        }
        nuint position = 0;
        while (position < count)
        {
            nuint take = count - position;
            nuint remaining = capacity - used;
            if (take > remaining) take = remaining;
            for (nuint i = 0; i < take; i++) scratch[used + i] = data[position + i];
            used += take; position += take;
            if (used == capacity) this.Flush();
        }
    }

    /// Drains this buffer without flushing the underlying device or closing the sink.
    /// A sink exception propagates and makes later Write and Flush calls reject.
    public void Flush()
    {
        if (failed) throw new Exception("Buffered writer cannot continue after output failure");
        if (used == 0) return;
        failed = true;
        output.Write(scratch, used);
        used = 0;
        failed = false;
    }
}
