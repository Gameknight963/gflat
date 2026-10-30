using std::Memory;
namespace std::Text;

// Borrow the two sides of the last exact counted byte-sequence separator.
// Output references must be distinct; receiver/separator metadata may be reused.
bool TrySplitLastOnce(Span<readonly(char)> text, readonly(char)* separator, nuint count,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitLastOnce(separator, count, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitLastOnce(readonly(char)* separator, nuint count, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        if (count == 0) return SplitLastOnceEmptyPattern((*this), ref before, ref after);
        if (count <= (*this).Length)
        {
            nuint position = ByteSearch::LastIndexOf(((*this).Data!).reinterpret<readonly(byte)*>(), (*this).Length,
                separator.reinterpret<readonly(byte)*>(), count);
            if (position != (*this).Length)
            {
                nuint end = position + count;
                Span<readonly(char)> first = (*this).Slice(0, position);
                Span<readonly(char)> rest = (*this).Slice(end, (*this).Length - end);
                before = first; after = rest;
                return true;
            }
        }
        before = new Span<readonly(char)>();
        after = new Span<readonly(char)>();
        return false;
    }
}

bool TrySplitLastOnce(Span<readonly(char)> text, readonly(char)* separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => text.TrySplitLastOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitLastOnce(readonly(char)* separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => (*this).TrySplitLastOnce(separator, CString::Length(separator), ref before, ref after);
}

bool TrySplitLastOnce(Span<readonly(char)> text, Span<readonly(char)> separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitLastOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitLastOnce(Span<readonly(char)> separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        if (separator.IsEmpty) return SplitLastOnceEmptyPattern((*this), ref before, ref after);
        return (*this).TrySplitLastOnce(separator.Data!, separator.Length, ref before, ref after);
    }
}

private bool SplitLastOnceEmptyPattern(Span<readonly(char)> text,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    Span<readonly(char)> first = text;
    Span<readonly(char)> rest = new();
    before = first; after = rest;
    return true;
}
