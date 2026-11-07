using Standard::Queries;

namespace Standard::Text;

private void WriteJoinedStrings<S, E>(ITextWriter* writer, Enumerable<String, S, E> values,
    readonly(char)* separator, nuint separatorCount, bool terminated)
{
    E iterator = values.GetEnumerator();
    bool first = true;
    while (iterator.MoveNext())
    {
        String value = iterator.Current;
        if (!first)
        {
            if (terminated)
            {
                while (separator[separatorCount] != '\0') separatorCount++;
                terminated = false;
            }
            if (separatorCount != 0) writer.Write(separator, separatorCount);
        }
        if (!value.IsEmpty) writer.Write(value.Data, value.Length);
        first = false;
    }
}

public void WriteJoin<S, E>(ITextWriter* writer, Enumerable<String, S, E> values,
    readonly(char)* separator, nuint separatorCount)
    => WriteJoinedStrings<S, E>(writer, move values, separator, separatorCount, false);

public void WriteJoin<S, E>(ITextWriter* writer, Enumerable<String, S, E> values, readonly(char)* separator)
    => WriteJoinedStrings<S, E>(writer, move values, separator, 0, true);

extension ITextWriter
{
    public void WriteJoin<S, E>(Enumerable<String, S, E> values,
        readonly(char)* separator, nuint separatorCount)
        => Standard::Text::WriteJoin<S, E>(this, move values, separator, separatorCount);

    public void WriteJoin<S, E>(Enumerable<String, S, E> values, readonly(char)* separator)
        => Standard::Text::WriteJoin<S, E>(this, move values, separator);
}
