namespace Standard::Encoding;

// Accepts strict padded Base64 text; writes decoded bytes to a borrowed sink.
public struct Base64Decoder : ITextWriter
{
    private ITextWriter* output;
    private char first;
    private char second;
    private char third;
    private nuint pending;
    private bool padded;
    private bool completed;
    private bool failed;

    public Base64Decoder(ITextWriter* destination)
    {
        output = destination;
        first = '\0';
        second = '\0';
        third = '\0';
        pending = 0;
        padded = false;
        completed = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("Base64 decoder cannot continue after failure");
    }

    public void Write(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        if (completed) throw new Exception("Base64 decoder is already complete");
        if (count == 0) return;
        failed = true;
        char[4] group = [first, second, third, (char)0];
        char[192] decoded;
        nuint used = 0;
        for (nuint index = 0; index < count; index++)
        {
            if (padded) throw new Exception("Unexpected data after Base64 padding");
            group[pending] = data[index];
            pending++;
            if (pending == 4)
            {
                nuint written = 0;
                if (!Base64::TryDecode(group, 4, (char*)decoded + used, 192 - used, ref written))
                    throw new Exception("Invalid Base64 input");
                pending = 0;
                padded = group[3] == '=';
                used += written;
                if (used == 192)
                {
                    output.Write(decoded, used);
                    used = 0;
                }
            }
        }
        if (pending != 0) first = group[0];
        if (pending >= 2) second = group[1];
        if (pending == 3) third = group[2];
        if (used != 0) output.Write(decoded, used);
        failed = false;
    }

    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        if (pending != 0) throw new Exception("Incomplete Base64 quartet");
        completed = true;
        failed = false;
    }
}
