namespace std::Text;

/// Matches the whole counted byte sequence using *, ?, and backslash escapes.
/// Case-sensitive, allocation-free, nonrecursive; worst-case O(length * count) work.
/// Both buffers are borrowed and may overlap; zero lengths do not read that buffer.
public bool MatchesWildcard(readonly(char)* text, nuint length, readonly(char)* pattern, nuint count) nothrow
{
    nuint position = 0;
    nuint token = 0;
    nuint starPattern = 0;
    nuint starText = 0;
    bool hasStar = false;
    while (position < length)
    {
        if (token < count)
        {
            char value = pattern[token];
            if (value == '*')
            {
                token++;
                if (token == count) return true;
                hasStar = true;
                starPattern = token;
                starText = position;
                continue;
            }
            nuint step = 1;
            bool matches = false;
            if (value == '\\')
            {
                if (count - token < 2) return false;
                value = pattern[token + 1];
                step = 2;
                matches = text[position] == value;
            }
            else if (value == '?') matches = true;
            else matches = text[position] == value;
            if (matches)
            {
                position++;
                token += step;
                continue;
            }
        }
        if (!hasStar || starText == length) return false;
        starText++;
        position = starText;
        token = starPattern;
    }
    // An empty suffix matches only remaining unescaped stars.
    while (token < count && pattern[token] == '*') token++;
    return token == count;
}
