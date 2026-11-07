using Standard::Memory;

namespace Standard::Text;

public void AppendJoin(ref String target, Span<immutable(String)> values,
    readonly(char)* separator, nuint separatorCount)
{
    String joined = Standard::Text::Join(values, separator, separatorCount);
    target.Append(&joined);
}

public void AppendJoin(ref String target, Span<immutable(String)> values, readonly(char)* separator)
{
    String joined = Standard::Text::Join(values, separator);
    target.Append(&joined);
}

extension String
{
    public void AppendJoin(Span<immutable(String)> values, readonly(char)* separator, nuint separatorCount)
        => Standard::Text::AppendJoin(ref *this, values, separator, separatorCount);

    public void AppendJoin(Span<immutable(String)> values, readonly(char)* separator)
        => Standard::Text::AppendJoin(ref *this, values, separator);
}
