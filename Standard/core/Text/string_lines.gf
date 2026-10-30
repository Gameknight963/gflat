using Standard::Memory;
namespace Standard::Text;

// The explicit ref overload requires existing storage and never copies the owner.
TextLines Lines(ref immutable(String) text)
    => new TextLines(new Span<readonly(char)>(text.Data, text.Length));

// The view borrows the owner's bytes; keep the owner alive, unmoved, and its storage stable.
// A temporary receiver lasts only through this full expression, not the view's lifetime.
extension String
{
    public immutable TextLines Lines()
    => Standard::Text::Lines(ref *this);
}
