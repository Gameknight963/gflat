namespace Standard::Csv;

// Borrows a destination; never flushes, closes, or disposes it.
public struct CsvWriter
{
    private ITextWriter* output;
    private bool hasField;
    private bool failed;

    public CsvWriter(ITextWriter* destination)
    {
        output = destination;
        hasField = false;
        failed = false;
    }

    private immutable void CheckUsable()
    {
        if (failed) throw new Exception("CSV writer cannot continue after output failure");
    }

    public void WriteField(readonly(char)* data, nuint count)
    {
        this.CheckUsable();
        bool quoted = false;
        for (nuint i = 0; i < count; i++)
        {
            char value = data[i];
            if (value == ',' || value == '"' || value == '\r' || value == '\n')
                quoted = true;
        }

        // A sink can throw after writing part of any call. Never resume a record
        // whose output might already be incomplete.
        failed = true;
        if (hasField) output.Write(",", 1);
        if (quoted)
        {
            output.Write("\"", 1);
            nuint start = 0;
            for (nuint i = 0; i < count; i++)
            {
                if (data[i] == '"')
                {
                    if (i != start) output.Write(data + start, i - start);
                    output.Write("\"\"", 2);
                    start = i + 1;
                }
            }
            if (start != count) output.Write(data + start, count - start);
            output.Write("\"", 1);
        }
        else if (count != 0) output.Write(data, count);
        hasField = true;
        failed = false;
    }

    // A record without explicit fields is represented by one empty field.
    public void WriteEndRecord()
    {
        this.CheckUsable();
        failed = true;
        output.Write("\r\n", 2);
        hasField = false;
        failed = false;
    }
}
