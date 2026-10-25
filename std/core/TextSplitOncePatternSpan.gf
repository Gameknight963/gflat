using std::Collections;
namespace std;

// Borrow the two sides of the first exact counted byte-sequence separator.
// Output references must be distinct; receiver metadata may be reused as either.
bool TrySplitOnce(this Span<readonly(char)> text, readonly(char)* separator, nuint count,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    if (count == 0) return SplitOnceEmptyPattern(text, ref before, ref after);
    if (count <= text.Length)
    {
        nuint position = ByteSearch::IndexOf((readonly(byte)*)text.Data!, text.Length,
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

bool TrySplitOnce(this Span<readonly(char)> text, readonly(char)* separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    => text.TrySplitOnce(separator, CString::Length(separator), ref before, ref after);

bool TrySplitOnce(this Span<readonly(char)> text, Span<readonly(char)> separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    if (separator.IsEmpty) return SplitOnceEmptyPattern(text, ref before, ref after);
    return text.TrySplitOnce(separator.Data!, separator.Length, ref before, ref after);
}

private bool SplitOnceEmptyPattern(Span<readonly(char)> text,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    Span<readonly(char)> first = new();
    Span<readonly(char)> rest = text;
    before = first; after = rest;
    return true;
}
