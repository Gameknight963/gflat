using std::Text;
namespace std;

// Borrow the counted bytes; parsing does not copy or modify the owning String.
int ParseInt(this ref immutable(String) text) => std::ParseInt(text.Data, text.Length);
bool TryParseInt(this ref immutable(String) text, int* result) nothrow
    => std::TryParseInt(text.Data, text.Length, result);
