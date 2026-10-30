using std::Memory;
namespace std::Text;

// Borrow a named String without copying or retaining a temporary receiver.
TextLines Lines(ref immutable(String) text)
    => new TextLines(new Span<readonly(char)>(text.Data, text.Length));

extension String
{
    public immutable TextLines Lines()
    => std::Text::Lines(ref *this);
}
