using Standard::Text;
namespace Standard::Text;

// Borrow the counted bytes; parsing does not copy or modify the owning String.
int ParseInt(ref immutable(String) text) => Standard::Text::ParseInt(text.Data, text.Length);

extension String
{
    public immutable int ParseInt()
    => Standard::Text::ParseInt(ref *this);
}
bool TryParseInt(ref immutable(String) text, int* result) nothrow
    => Standard::Text::TryParseInt(text.Data, text.Length, result);

extension String
{
    public immutable bool TryParseInt(int* result) nothrow
    => Standard::Text::TryParseInt(ref *this, result);
}
