namespace Standard::Encoding;

/// Decodes strict padded uppercase Base32 into counted bytes at a borrowed sink.
/// Complete validates the final group. Destruction never writes or closes the sink.
public struct Base32Decoder : ITextWriter
{
    private ITextWriter* output;
    private char[7] carry;
    private nuint pending;
    private bool padded;
    private bool completed;
    private bool failed;

    public Base32Decoder(ITextWriter* destination)
    {
        output = destination;
        carry = ['\0', '\0', '\0', '\0', '\0', '\0', '\0'];
        pending = 0;
        padded = false;
        completed = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("Base32 decoder cannot continue after failure");
    }

    /// Accepts arbitrary counted chunks, retaining at most seven copied encoded bytes.
    /// Input must be stable and disjoint from decoder and storage the sink can mutate.
    /// A valid prefix may already have reached the sink before a later error.
    public void Write(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        if (completed) throw new Exception("Base32 decoder is already complete");
        if (count == 0) return;
        failed = true;
        char[8] group;
        for (nuint i = 0; i < pending; i++) group[i] = carry[i];
        char[200] decoded;
        nuint used = 0;
        for (nuint index = 0; index < count; index++)
        {
            if (padded) throw new Exception("Unexpected data after Base32 padding");
            group[pending] = data[index];
            pending++;
            if (pending == 8)
            {
                nuint written = 0;
                if (!Base32::TryDecode(group, 8, (char*)decoded + used, 200 - used, ref written))
                    throw new Exception("Invalid Base32 input");
                pending = 0;
                padded = group[7] == '=';
                used += written;
                if (used == 200)
                {
                    output.Write(decoded, used);
                    used = 0;
                }
            }
        }
        for (nuint i = 0; i < pending; i++) carry[i] = group[i];
        if (used != 0) output.Write(decoded, used);
        failed = false;
    }

    /// Rejects an incomplete final group. Successful completion emits nothing.
    /// Repeated success is idempotent and every failure permanently rejects reuse.
    public void Complete()
    {
        this.CheckUsable();
        if (completed) return;
        failed = true;
        if (pending != 0) throw new Exception("Incomplete Base32 group");
        completed = true;
        failed = false;
    }
}
