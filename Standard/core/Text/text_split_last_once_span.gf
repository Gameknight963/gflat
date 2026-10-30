using Standard::Memory;
namespace Standard::Text;

// Borrow both sides of the last separator; output references must be distinct.
bool TrySplitLastOnce(Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitLastOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public immutable bool TrySplitLastOnce(char separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        Span<readonly(char)> protectedView = *this;
        nuint end = protectedView.Length;
        while (end != 0)
        {
            end--;
            if (protectedView[end] != separator) continue;
            Span<readonly(char)> first = protectedView.Slice(0, end);
            Span<readonly(char)> rest = protectedView.Slice(end + 1, protectedView.Length - end - 1);
            before = first; after = rest;
            return true;
        }
        before = new Span<readonly(char)>();
        after = new Span<readonly(char)>();
        return false;
    }
}
