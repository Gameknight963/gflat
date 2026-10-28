using std::Memory;
namespace std::Text;

// Borrow the two sides of the last exact counted byte-sequence separator.
// Output references must be distinct; receiver/separator metadata may be reused.
bool TrySplitLastOnce(this Span<readonly(char)> text, readonly(char)* separator, nuint count,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    if (count == 0) return SplitLastOnceEmptyPattern(text, ref before, ref after);
    if (count <= text.Length)
    {
        nuint position = ByteSearch::LastIndexOf((readonly(byte)*)text.Data!, text.Length,
            (readonly(byte)*)separator, count);
        if (position != text.Length)
        {
            nuint end = position + count;
            Span<readonly(char)> first = text.Slice(0, position);
            Span<readonly(char)> rest = text.Slice(end, text.Length - end);
            before = first; after = rest;
            return true;
        }
    }
    before = new Span<readonly(char)>();
    after = new Span<readonly(char)>();
    return false;
}

bool TrySplitLastOnce(this Span<readonly(char)> text, readonly(char)* separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => text.TrySplitLastOnce(separator, CString::Length(separator), ref before, ref after);

bool TrySplitLastOnce(this Span<readonly(char)> text, Span<readonly(char)> separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    if (separator.IsEmpty) return SplitLastOnceEmptyPattern(text, ref before, ref after);
    return text.TrySplitLastOnce(separator.Data!, separator.Length, ref before, ref after);
}

private bool SplitLastOnceEmptyPattern(Span<readonly(char)> text,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    Span<readonly(char)> first = text;
    Span<readonly(char)> rest = new();
    before = first; after = rest;
    return true;
}
