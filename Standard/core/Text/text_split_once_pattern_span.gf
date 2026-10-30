using Standard::Text;
using Standard::Memory;
using Standard::Collections;
namespace Standard::Text;

// Borrow the two sides of the first exact counted byte-sequence separator.
// Output references must be distinct; receiver metadata may be reused as either.
bool TrySplitOnce(Span<readonly(char)> text, readonly(char)* separator, nuint count,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, count, ref before, ref after);

extension Span<readonly(char)>
{
    public immutable bool TrySplitOnce(readonly(char)* separator, nuint count, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        Span<readonly(char)> protectedView = *this;
        if (count == 0) return SplitOnceEmptyPattern(protectedView, ref before, ref after);
        if (count <= protectedView.Length)
        {
            nuint position = ByteSearch::IndexOf((protectedView.Data!).reinterpret<readonly(byte)*>(), protectedView.Length,
                separator.reinterpret<readonly(byte)*>(), count);
            if (position != protectedView.Length)
            {
                nuint end = position + count;
                Span<readonly(char)> first = protectedView.Slice(0, position);
                Span<readonly(char)> rest = protectedView.Slice(end, protectedView.Length - end);
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
    public immutable bool TrySplitOnce(readonly(char)* separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => (*this).TrySplitOnce(separator, CString::Length(separator), ref before, ref after);
}

bool TrySplitOnce(Span<readonly(char)> text, Span<readonly(char)> separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public immutable bool TrySplitOnce(Span<readonly(char)> separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        Span<readonly(char)> protectedView = *this;
        if (separator.IsEmpty) return SplitOnceEmptyPattern(protectedView, ref before, ref after);
        return protectedView.TrySplitOnce(separator.Data!, separator.Length, ref before, ref after);
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
