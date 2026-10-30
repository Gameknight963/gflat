namespace std::Encoding;

/// Streams canonical uppercase padded Base32 to one borrowed synchronous sink.
/// Complete emits the final padding; destruction never writes or closes the sink.
public struct Base32Encoder : ITextWriter
{
    private ITextWriter* output;
    private char[4] carry;
    private nuint pending;
    private bool completed;
    private bool failed;

    public Base32Encoder(ITextWriter* destination)
    {
        output = destination;
        carry = ['\0', '\0', '\0', '\0'];
        pending = 0;
        completed = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("Base32 writer cannot continue after output failure");
    }

    // At most 160 input bytes fit in the 256-byte encoded output buffer.
    // Write supplies only complete five-byte groups; Complete supplies the sole tail.
    private void WriteBlock(readonly(char)* data, nuint count)
    {
        char[256] encoded;
        nuint written = 0;
        if (!Base32::TryEncode(data, count, encoded, 256, ref written))
            throw new Exception("Base32 writer block encoding failed");
        output.Write(encoded, written);
    }

    /// Accepts counted binary bytes, retaining at most four copied bytes between calls.
    /// Input must be stable and disjoint from encoder and storage the sink can mutate.
    public void Write(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        if (completed) throw new Exception("Base32 writer is already complete");
        if (count == 0) return;
        failed = true;
        nuint index = 0;
        if (pending != 0)
        {
            char[5] group;
            for (nuint i = 0; i < pending; i++) group[i] = carry[i];
            while (pending < 5 && index < count)
            {
                group[pending] = data[index];
                pending++;
                index++;
            }
            if (pending == 5)
            {
                this.WriteBlock(group, 5);
                pending = 0;
            }
            else
            {
                for (nuint i = 0; i < pending; i++) carry[i] = group[i];
                failed = false;
                return;
            }
        }
        while (count - index >= 5)
        {
            nuint amount = count - index;
            if (amount > 160) amount = 160;
            amount -= amount % 5;
            this.WriteBlock(data + index, amount);
            index += amount;
        }
        pending = count - index;
        for (nuint i = 0; i < pending; i++) carry[i] = data[index + i];
        failed = false;
    }

    /// Emits canonical padding for a pending tail, without flushing or closing output.
    /// Repeated success is idempotent; sink failure permanently rejects further use.
    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        if (pending != 0)
        {
            this.WriteBlock(carry, pending);
            pending = 0;
        }
        completed = true;
        failed = false;
    }
}
