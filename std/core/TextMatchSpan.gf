namespace std;

// Counted byte comparisons over borrowed text; no terminator is read from text.
bool StartsWith(this Span<readonly(char)> text, readonly(char)* prefix, nuint count)
{
    if (count > text.Length) return false;
    for (nuint i = 0; i < count; i++)
        if (text[i] != prefix[i]) return false;
    return true;
}

bool StartsWith(this Span<readonly(char)> text, readonly(char)* prefix)
    => text.StartsWith(prefix, CString::Length(prefix));

bool EndsWith(this Span<readonly(char)> text, readonly(char)* suffix, nuint count)
{
    if (count > text.Length) return false;
    nuint start = text.Length - count;
    for (nuint i = 0; i < count; i++)
        if (text[start + i] != suffix[i]) return false;
    return true;
}

bool EndsWith(this Span<readonly(char)> text, readonly(char)* suffix)
    => text.EndsWith(suffix, CString::Length(suffix));
