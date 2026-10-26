using std::Memory;
namespace std;

// Borrow both sides of the first separator; output references must be distinct.
bool TrySplitOnce(this Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
{
    for (nuint index = 0; index < text.Length; index++)
    {
        if (text[index] != separator) continue;
        Span<readonly(char)> first = text.Slice(0, index);
        Span<readonly(char)> rest = text.Slice(index + 1, text.Length - index - 1);
        before = first; after = rest;
        return true;
    }
    before = new Span<readonly(char)>();
    after = new Span<readonly(char)>();
    return false;
}
