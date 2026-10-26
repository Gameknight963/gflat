using std::Text;
namespace std::Text;

// Borrow the counted bytes; parsing does not copy or modify the owning String.
int ParseInt(this ref immutable(String) text) => std::Text::ParseInt(text.Data, text.Length);
bool TryParseInt(this ref immutable(String) text, int* result) nothrow
    => std::Text::TryParseInt(text.Data, text.Length, result);
