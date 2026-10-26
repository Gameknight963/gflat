using std::Memory;
namespace std::Text;

// Borrow an owning String without copying it or accepting a temporary receiver.
TextParts Split(this ref immutable(String) text, char delimiter, bool removeEmpty = false)
    => new TextParts(new Span<readonly(char)>(text.Data, text.Length), delimiter, removeEmpty);
