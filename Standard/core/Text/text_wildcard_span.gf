using Standard::Memory;
namespace Standard::Text;

public bool MatchesWildcard(Span<readonly(char)> text, readonly(char)* pattern, nuint count) nothrow
=> text.MatchesWildcard(pattern, count);

extension Span<readonly(char)>
{
    public immutable bool MatchesWildcard(readonly(char)* pattern, nuint count) nothrow
    {
        Span<readonly(char)> protectedView = *this;
        readonly(char)* data = "";
        if (protectedView.Length != 0) data = protectedView.Data!;
        return Standard::Text::MatchesWildcard(data, protectedView.Length, pattern, count);
    }
}

public bool MatchesWildcard(Span<readonly(char)> text, Span<readonly(char)> pattern) nothrow
=> text.MatchesWildcard(pattern);

extension Span<readonly(char)>
{
    public immutable bool MatchesWildcard(Span<readonly(char)> pattern) nothrow
    {
        readonly(char)* data = "";
        if (pattern.Length != 0) data = pattern.Data!;
        return (*this).MatchesWildcard(data, pattern.Length);
    }
}

public bool MatchesWildcard(Span<readonly(char)> text, readonly(char)* pattern) nothrow
    => text.MatchesWildcard(pattern);

extension Span<readonly(char)>
{
    public immutable bool MatchesWildcard(readonly(char)* pattern) nothrow
    => (*this).MatchesWildcard(pattern, CString::Length(pattern));
}
