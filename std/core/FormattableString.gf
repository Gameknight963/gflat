namespace std;

// Borrow the formatter, including noncopyable values. The returned String owns its bytes.
public String ToString<T : IFormattable>(this ref immutable(T) value)
{
    String result = new();
    value.Format(&result);
    return move result;
}
