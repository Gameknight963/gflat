namespace std::Encoding;

// Streams padded Base64 to a borrowed sink. Complete emits the final padding.
public struct Base64Encoder : ITextWriter
{
    private ITextWriter* output;
    private char first;
    private char second;
    private nuint pending;
    private bool completed;
    private bool failed;

    public Base64Encoder(ITextWriter* destination)
    {
        output = destination;
        first = '\0';
        second = '\0';
        pending = 0;
        completed = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("Base64 writer cannot continue after output failure");
    }

    // All calls contain at most 192 bytes. Only Complete passes a partial group.
    private void WriteBlock(readonly(char)* data, nuint count)
    {
        char[256] encoded;
        nuint written = 0;
        if (!Base64::TryEncode(data, count, encoded, 256, ref written))
            throw new Exception("Base64 writer block encoding failed");
        output.Write(encoded, written);
    }

    public void Write(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        if (completed) throw new Exception("Base64 writer is already complete");
        if (count == 0) return;
        failed = true;
        nuint index = 0;
        if (pending != 0)
        {
            char[3] group;
            group[0] = first;
            if (pending == 2) group[1] = second;
            while (pending < 3 && index < count)
            {
                group[pending] = data[index];
                pending++;
                index++;
            }
            if (pending == 3)
            {
                this.WriteBlock(group, 3);
                pending = 0;
            }
            else
            {
                first = group[0];
                if (pending == 2) second = group[1];
                failed = false;
                return;
            }
        }
        while (count - index >= 3)
        {
            nuint amount = count - index;
            if (amount > 192) amount = 192;
            amount -= amount % 3;
            this.WriteBlock(data + index, amount);
            index += amount;
        }
        pending = count - index;
        if (pending != 0) first = data[index];
        if (pending == 2) second = data[index + 1];
        failed = false;
    }

    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        if (pending != 0)
        {
            char[2] tail = [first, second];
            this.WriteBlock(tail, pending);
            pending = 0;
        }
        completed = true;
        failed = false;
    }
}
