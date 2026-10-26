using std::Memory;
namespace std::Text;

// Borrow a named String without copying or retaining a temporary receiver.
TextLines Lines(this ref immutable(String) text)
    => new TextLines(new Span<readonly(char)>(text.Data, text.Length));
