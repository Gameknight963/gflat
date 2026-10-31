namespace Standard::Encoding;

// Streams padded uppercase Base32 to a borrowed writer.
// Call Complete to write final padding. Keep the destination alive through completion.
// Destruction discards the pending tail.
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

    // Encodes binary chunks, retaining at most four pending bytes.
    // Input must be separate from encoder state and storage the sink can mutate.
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

    // Writes padding for the pending tail. Repeated successful completion is allowed.
    // A sink failure permanently prevents further use.
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
