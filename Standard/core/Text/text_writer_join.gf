using Standard::Memory;

namespace Standard::Text;

// Streams borrowed Strings with separators between entries, including empty entries.
// Keep values and separator stable and separate from storage the writer modifies.
// Writer errors propagate and can leave a written prefix.
public void WriteJoin(ITextWriter* writer, Span<immutable(String)> values,
    readonly(char)* separator, nuint separatorCount)
{
    for (nuint index = 0; index < values.Length; index++)
    {
        if (index != 0 && separatorCount != 0) writer.Write(separator, separatorCount);
        nuint count = values[index].Length;
        if (count != 0) writer.Write(values[index].Data, count);
    }
}

public void WriteJoin(ITextWriter* writer, Span<immutable(String)> values, readonly(char)* separator)
{
    nuint count = 0;
    if (values.Length > 1)
        while (separator[count] != '\0') count++;
    Standard::Text::WriteJoin(writer, values, separator, count);
}

extension ITextWriter
{
    public void WriteJoin(Span<immutable(String)> values, readonly(char)* separator, nuint separatorCount)
        => Standard::Text::WriteJoin(this, values, separator, separatorCount);

    public void WriteJoin(Span<immutable(String)> values, readonly(char)* separator)
        => Standard::Text::WriteJoin(this, values, separator);
}
