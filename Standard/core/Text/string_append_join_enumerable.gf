using Standard::Queries;

namespace Standard::Text;

public void AppendJoin<S, E>(ref String target, Enumerable<String, S, E> values,
    readonly(char)* separator, nuint separatorCount)
{
    String joined = Standard::Text::Join<S, E>(move values, separator, separatorCount);
    target.Append(&joined);
}

public void AppendJoin<S, E>(ref String target, Enumerable<String, S, E> values, readonly(char)* separator)
{
    String joined = Standard::Text::Join<S, E>(move values, separator);
    target.Append(&joined);
}

extension String
{
    public void AppendJoin<S, E>(Enumerable<String, S, E> values,
        readonly(char)* separator, nuint separatorCount)
        => Standard::Text::AppendJoin<S, E>(ref *this, move values, separator, separatorCount);

    public void AppendJoin<S, E>(Enumerable<String, S, E> values, readonly(char)* separator)
        => Standard::Text::AppendJoin<S, E>(ref *this, move values, separator);
}
