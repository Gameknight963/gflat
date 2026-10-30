namespace Standard::Text;

/// Matches bytes before each readable zero; literal NUL requires the counted overload.
public bool MatchesWildcard(readonly(char)* text, readonly(char)* pattern) nothrow
    => Standard::Text::MatchesWildcard(text, CString::Length(text), pattern, CString::Length(pattern));
