using std::Text;
namespace std::Text;

// Borrow the counted bytes; parsing does not copy or modify the owning String.
int ParseInt(ref immutable(String) text) => std::Text::ParseInt(text.Data, text.Length);

extension String
{
    public immutable int ParseInt()
    => std::Text::ParseInt(ref *this);
}
bool TryParseInt(ref immutable(String) text, int* result) nothrow
    => std::Text::TryParseInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseInt(int* result) nothrow
    => std::Text::TryParseInt(ref *this, result);
}
