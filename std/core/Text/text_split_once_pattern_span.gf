using std::Text;
using std::Memory;
using std::Collections;
namespace std::Text;

// Borrow the two sides of the first exact counted byte-sequence separator.
// Output references must be distinct; receiver metadata may be reused as either.
bool TrySplitOnce(Span<readonly(char)> text, readonly(char)* separator, nuint count,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, count, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitOnce(readonly(char)* separator, nuint count, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        if (count == 0) return SplitOnceEmptyPattern((*this), ref before, ref after);
        if (count <= (*this).Length)
        {
            nuint position = ByteSearch::IndexOf(((*this).Data!).reinterpret<readonly(byte)*>(), (*this).Length,
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

bool TrySplitOnce(Span<readonly(char)> text, readonly(char)* separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => text.TrySplitOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitOnce(readonly(char)* separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => (*this).TrySplitOnce(separator, CString::Length(separator), ref before, ref after);
}

bool TrySplitOnce(Span<readonly(char)> text, Span<readonly(char)> separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitOnce(Span<readonly(char)> separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        if (separator.IsEmpty) return SplitOnceEmptyPattern((*this), ref before, ref after);
        return (*this).TrySplitOnce(separator.Data!, separator.Length, ref before, ref after);
    }
}

private bool SplitOnceEmptyPattern(Span<readonly(char)> text,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    Span<readonly(char)> first = new();
    Span<readonly(char)> rest = text;
    before = first; after = rest;
    return true;
}
