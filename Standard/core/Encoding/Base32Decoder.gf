namespace Standard::Encoding;

// Streams strict padded uppercase Base32 into a borrowed writer.
// Call Complete to validate the final group. Keep the destination alive through completion.
// Destruction discards pending input.
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

    // Decodes encoded chunks, retaining at most seven pending bytes.
    // Input must be separate from decoder state and storage the sink can mutate.
    // A later error can leave a valid prefix already written.
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

    // Rejects an incomplete final group. Repeated successful completion is allowed.
    // A failure permanently prevents further use.
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
