using std::Memory;
namespace std::Text;

public bool MatchesWildcard(this Span<readonly(char)> text, readonly(char)* pattern, nuint count) nothrow
{
    readonly(char)* data = "";
    if (text.Length != 0) data = text.Data!;
    return std::Text::MatchesWildcard(data, text.Length, pattern, count);
}

public bool MatchesWildcard(this Span<readonly(char)> text, Span<readonly(char)> pattern) nothrow
{
    readonly(char)* data = "";
    if (pattern.Length != 0) data = pattern.Data!;
    return text.MatchesWildcard(data, pattern.Length);
}

public bool MatchesWildcard(this Span<readonly(char)> text, readonly(char)* pattern) nothrow
    => text.MatchesWildcard(pattern, CString::Length(pattern));
