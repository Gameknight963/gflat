using Standard::Queries;

namespace Standard::Text;

private String JoinStrings<S, E>(Enumerable<String, S, E> values,
    readonly(char)* separator, nuint separatorCount, bool terminated)
{
    String result = new();
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
            result.Append(separator, separatorCount);
        }
        result.Append(value.Data, value.Length);
        first = false;
    }
    return move result;
}

public String Join<S, E>(Enumerable<String, S, E> values,
    readonly(char)* separator, nuint separatorCount)
    => JoinStrings<S, E>(move values, separator, separatorCount, false);

public String Join<S, E>(Enumerable<String, S, E> values, readonly(char)* separator)
    => JoinStrings<S, E>(move values, separator, 0, true);

extension Enumerable<Standard::Text::String, S, E>
{
    public String Join(readonly(char)* separator, nuint separatorCount)
        => Standard::Text::Join<S, E>(*this, separator, separatorCount);

    public String Join(readonly(char)* separator)
        => Standard::Text::Join<S, E>(*this, separator);
}
