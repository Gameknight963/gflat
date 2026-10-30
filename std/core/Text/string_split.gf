using std::Memory;
namespace std::Text;

// Borrow an owning String without copying it or accepting a temporary receiver.
TextParts Split(ref immutable(String) text, char delimiter, bool removeEmpty = false)
    => new TextParts(new Span<readonly(char)>(text.Data, text.Length), delimiter, removeEmpty);

extension String
{
    public immutable TextParts Split(char delimiter, bool removeEmpty = false)
    => std::Text::Split(ref *this, delimiter, removeEmpty);
}
