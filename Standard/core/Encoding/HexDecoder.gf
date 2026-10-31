namespace Standard::Encoding;

// Streams hexadecimal pairs into a borrowed writer.
// Call Complete to check the final length. Any input or sink failure prevents reuse.
public struct HexDecoder : ITextWriter
{
    private ITextWriter* output;
    private char first;
    private nuint pending;
    private bool completed;
    private bool failed;

    public HexDecoder(ITextWriter* destination) nothrow
    {
        output = destination;
        first = '\0';
        pending = 0;
        completed = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("Hex decoder cannot continue after failure");
    }

    // Decodes chunks, retaining one pending hex digit until its pair arrives.
    public void Write(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        if (completed) throw new Exception("Hex decoder is already complete");
        if (count == 0) return;
        failed = true;
        char[2] group = [first, (char)0];
        char[256] decoded;
        nuint used = 0;
        for (nuint index = 0; index < count; index++)
        {
            group[pending] = data[index];
            pending++;
            if (pending == 2)
            {
                nuint written = 0;
                if (!Hex::TryDecode(group, 2, (char*)decoded + used, 256 - used, ref written))
                    throw new Exception("Invalid hexadecimal input");
                pending = 0;
                used += written;
                if (used == 256)
                {
                    output.Write(decoded, used);
                    used = 0;
                }
            }
        }
        if (pending != 0) first = group[0];
        if (used != 0) output.Write(decoded, used);
        failed = false;
    }

    // Rejects an unmatched final hex digit.
    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        if (pending != 0) throw new Exception("Incomplete hexadecimal byte");
        completed = true;
        failed = false;
    }
}
