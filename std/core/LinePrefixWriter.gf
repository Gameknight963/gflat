namespace std;

// Borrows both the sink and counted prefix. LF alone starts the next input line.
public struct LinePrefixWriter : ITextWriter
{
    private ITextWriter* output;
    private readonly(char)* prefix;
    private nuint prefixLength;
    private bool lineStart;
    private bool failed;

    public LinePrefixWriter(ITextWriter* destination, readonly(char)* prefix, nuint count)
    {
        output = destination;
        this.prefix = prefix;
        prefixLength = count;
        lineStart = true;
        failed = false;
    }

    public void Write(readonly(char)* data, nuint count)
    {
        if (failed) throw new Exception("Line prefix writer cannot continue after output failure");
        if (count == 0) return;
        failed = true;
        nuint start = 0;
        while (start < count)
        {
            if (lineStart && prefixLength != 0) output.Write(prefix, prefixLength);
            nuint end = start;
            while (end < count && data[end] != '\n') end++;
            if (end < count) end++;
            output.Write(data + start, end - start);
            lineStart = data[end - 1] == '\n';
            start = end;
        }
        failed = false;
    }
}
