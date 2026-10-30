using std::Memory;
namespace std::Text;

// Borrow both sides of the last separator; output references must be distinct.
bool TrySplitLastOnce(Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitLastOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitLastOnce(char separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        nuint end = (*this).Length;
        while (end != 0)
        {
            end--;
            if ((*this)[end] != separator) continue;
            Span<readonly(char)> first = (*this).Slice(0, end);
            Span<readonly(char)> rest = (*this).Slice(end + 1, (*this).Length - end - 1);
            before = first; after = rest;
            return true;
        }
        before = new Span<readonly(char)>();
        after = new Span<readonly(char)>();
        return false;
    }
}
