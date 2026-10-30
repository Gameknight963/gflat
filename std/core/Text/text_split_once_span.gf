using std::Memory;
namespace std::Text;

// Borrow both sides of the first separator; output references must be distinct.
bool TrySplitOnce(Span<readonly(char)> text, char separator,
    ref Span<readonly(char)> before, ref Span<readonly(char)> after)
=> text.TrySplitOnce(separator, ref before, ref after);

extension Span<readonly(char)>
{
    public bool TrySplitOnce(char separator, ref Span<readonly(char)> before, ref Span<readonly(char)> after)
    {
        for (nuint index = 0; index < (*this).Length; index++)
        {
            if ((*this)[index] != separator) continue;
            Span<readonly(char)> first = (*this).Slice(0, index);
            Span<readonly(char)> rest = (*this).Slice(index + 1, (*this).Length - index - 1);
            before = first; after = rest;
            return true;
        }
        before = new Span<readonly(char)>();
        after = new Span<readonly(char)>();
        return false;
    }
}
