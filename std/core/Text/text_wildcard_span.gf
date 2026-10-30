using std::Memory;
namespace std::Text;

public bool MatchesWildcard(Span<readonly(char)> text, readonly(char)* pattern, nuint count) nothrow
=> text.MatchesWildcard(pattern, count);

extension Span<readonly(char)>
{
    public bool MatchesWildcard(readonly(char)* pattern, nuint count) nothrow
    {
        readonly(char)* data = "";
        if ((*this).Length != 0) data = (*this).Data!;
        return std::Text::MatchesWildcard(data, (*this).Length, pattern, count);
    }
}

public bool MatchesWildcard(Span<readonly(char)> text, Span<readonly(char)> pattern) nothrow
=> text.MatchesWildcard(pattern);

extension Span<readonly(char)>
{
    public bool MatchesWildcard(Span<readonly(char)> pattern) nothrow
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
    public bool MatchesWildcard(readonly(char)* pattern) nothrow
    => (*this).MatchesWildcard(pattern, CString::Length(pattern));
}
