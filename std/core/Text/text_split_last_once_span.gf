using std::Memory;
namespace std::Text;

// Borrow both sides of the last separator; output references must be distinct.
bool TrySplitLastOnce(this Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    nuint end = text.Length;
    while (end != 0)
    {
        end--;
        if (text[end] != separator) continue;
        Span<readonly(char)> first = text.Slice(0, end);
        Span<readonly(char)> rest = text.Slice(end + 1, text.Length - end - 1);
        before = first; after = rest;
        return true;
    }
    before = new Span<readonly(char)>();
    after = new Span<readonly(char)>();
    return false;
}
