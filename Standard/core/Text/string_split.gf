using Standard::Memory;
namespace Standard::Text;

// The explicit ref overload requires existing storage and never copies the owner.
TextParts Split(ref immutable(String) text, char delimiter, bool removeEmpty = false)
    => new TextParts(new Span<readonly(char)>(text.Data, text.Length), delimiter, removeEmpty);

// The view borrows the owner's bytes; keep the owner alive, unmoved, and its storage stable.
// A temporary receiver lasts only through this full expression, not the view's lifetime.
extension String
{
    public immutable TextParts Split(char delimiter, bool removeEmpty = false)
    => Standard::Text::Split(ref *this, delimiter, removeEmpty);
}
