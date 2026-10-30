using std::Text;
namespace std::Text;

// Borrow the formatter, including noncopyable values. The returned String owns its bytes.
public String ToString<T : IFormattable>(ref immutable(T) value)
{
    String result = new();
    value.Format(&result);
    return move result;
}

extension<T : IFormattable> T
{
    public immutable String ToString()
    => std::Text::ToString<T>(ref *this);
}
