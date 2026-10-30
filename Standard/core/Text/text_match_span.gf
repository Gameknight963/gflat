using Standard::Text;
using Standard::Memory;
namespace Standard::Text;

// Counted byte comparisons over borrowed text; no terminator is read from text.
bool StartsWith(Span<readonly(char)> text, readonly(char)* prefix, nuint count)
=> text.StartsWith(prefix, count);

extension Span<readonly(char)>
{
    public immutable bool StartsWith(readonly(char)* prefix, nuint count)
    {
        if (count > (*this).Length) return false;
        for (nuint i = 0; i < count; i++)
            if ((*this)[i] != prefix[i]) return false;
        return true;
    }
}

bool StartsWith(Span<readonly(char)> text, readonly(char)* prefix)
    => text.StartsWith(prefix);

extension Span<readonly(char)>
{
    public immutable bool StartsWith(readonly(char)* prefix)
    => (*this).StartsWith(prefix, CString::Length(prefix));
}

bool EndsWith(Span<readonly(char)> text, readonly(char)* suffix, nuint count)
=> text.EndsWith(suffix, count);

extension Span<readonly(char)>
{
    public immutable bool EndsWith(readonly(char)* suffix, nuint count)
    {
        if (count > (*this).Length) return false;
        nuint start = (*this).Length - count;
        for (nuint i = 0; i < count; i++)
            if ((*this)[start + i] != suffix[i]) return false;
        return true;
    }
}

bool EndsWith(Span<readonly(char)> text, readonly(char)* suffix)
    => text.EndsWith(suffix);

extension Span<readonly(char)>
{
    public immutable bool EndsWith(readonly(char)* suffix)
    => (*this).EndsWith(suffix, CString::Length(suffix));
}
