using Standard::Memory;
namespace Standard::Text;

// Borrow both sides of the first separator; output references must be distinct.
bool TrySplitOnce(Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public immutable bool TrySplitOnce(char separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        Span<readonly(char)> protectedView = *this;
        for (nuint index = 0; index < protectedView.Length; index++)
        {
            if (protectedView[index] != separator) continue;
            Span<readonly(char)> first = protectedView.Slice(0, index);
            Span<readonly(char)> rest = protectedView.Slice(index + 1, protectedView.Length - index - 1);
            before = first; after = rest;
            return true;
        }
        before = new Span<readonly(char)>();
        after = new Span<readonly(char)>();
        return false;
    }
}
